using Script.Job.BuildLodOther;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Script.PathFind
{
    public partial struct MapData
    {
        public JobHandle Build(NativeArray<ObstacleType> obstacleMap)
        {
            ObstacleMap = obstacleMap;
            var groupLodInfo = new GroupLodInfo(MapDataInfo, MapDataInfo.StartLod);
            var job = new FindFirstLodGroupJob
            {
                GroupInfoMap = GroupInfoMap.AsParallelWriter(),
                EdgeMap = EdgeMap.AsParallelWriter(),
                ObstacleMap = ObstacleMap,
                FirstLodGroupIdIndexMap = FirstLodGroupIdIndexMap,
                BatchToGroupIdMap = BatchToGroupIdMap.AsParallelWriter(),
                GroupLodInfo = groupLodInfo
            };
            var findFirstLodGroupJob = job.Schedule(groupLodInfo.MapBatchShape.ToSize(), 1);
            findFirstLodGroupJob.Complete();

            var buildFirstEdgeJob = new BuildFirstEdgeMap
            {
                EdgeMap = EdgeMap.AsParallelWriter(),
                GroupLodInfo = groupLodInfo,
                FirstLodGroupIdIndexMap = FirstLodGroupIdIndexMap,
                GroupMap = GroupInfoMap
            };
            var buildFirstEdgeJobHandle = buildFirstEdgeJob.Schedule(groupLodInfo.MapBatchShape.ToSize(), 32, findFirstLodGroupJob);


            // 新的父节点及其子节点列表
            NativeParallelMultiHashMap<GroupId, GroupId> tempCombineGroupInfoMap
                = new NativeParallelMultiHashMap<GroupId, GroupId>(MapDataInfo.AllGroupShape.ToSize(), Allocator.TempJob);

            // 新的批次节点及其包含节点列表
            NativeParallelMultiHashMap<GroupId, GroupInfo> tempBatchToGroupIdMap
                = new NativeParallelMultiHashMap<GroupId, GroupInfo>(MapDataInfo.AllGroupShape.ToSize(), Allocator.TempJob);

            // 跨越批次的边集合
            NativeParallelMultiHashMap<GroupId, GroupId> tempCrossBatchGroup =
                new NativeParallelMultiHashMap<GroupId, GroupId>(MapDataInfo.AllGroupShape.ToSize(), Allocator.TempJob);

            // 同batch下组的边集合
            NativeParallelMultiHashMap<GroupId, GroupId> tempGroupEdgeGroup =
                new NativeParallelMultiHashMap<GroupId, GroupId>(MapDataInfo.AllGroupShape.ToSize(), Allocator.TempJob);


            var lastJob = buildFirstEdgeJobHandle;

            for (int i = MapDataInfo.StartLod + 1; i < MapDataInfo.MaxLod + 1; i++)
            {
                var lastGroupLodInfo = new GroupLodInfo(MapDataInfo, i - 1);
                var currentGroupLodInfo = new GroupLodInfo(MapDataInfo, i);

                var findCombineGroupJob = new FindCombineGroupJob()
                {
                    CurrentGroupLodInfo = currentGroupLodInfo,
                    LastGroupLodInfo = lastGroupLodInfo,
                    GroupInfoMap = GroupInfoMap,
                    EdgeMap = EdgeMap,
                    BatchToGroupIdMap = BatchToGroupIdMap,
                    TempCombineGroupInfoMap = tempCombineGroupInfoMap.AsParallelWriter(),
                    TempBatchToGroupIdMap = tempBatchToGroupIdMap.AsParallelWriter(),
                    TempCrossBatchGroup = tempCrossBatchGroup.AsParallelWriter(),
                    TempGroupEdgeGroup = tempGroupEdgeGroup.AsParallelWriter()
                };
                lastJob = findCombineGroupJob.Schedule(currentGroupLodInfo.MapBatchShape.ToSize(), 32, lastJob);
                var setLastGroupParentJob = new SetLastGroupParentJob()
                {
                    CurrentGroupLodInfo = currentGroupLodInfo,
                    TempCombineGroupIdMap = tempCombineGroupInfoMap,
                    TempBatchToGroupIdMap = tempBatchToGroupIdMap,
                    GroupInfoMap = GroupInfoMap,
                    BatchToGroupIdMap = BatchToGroupIdMap.AsParallelWriter()
                };

                lastJob = setLastGroupParentJob.Schedule(currentGroupLodInfo.MapBatchShape.ToSize(), 32, lastJob);
                var writeGroupJob = new WriteGroupJob()
                {
                    CurrentGroupLodInfo = currentGroupLodInfo,
                    GroupInfoMap = GroupInfoMap.AsParallelWriter(),
                    BatchToGroupIdMap = BatchToGroupIdMap.AsParallelWriter(),
                    TempBatchToGroupIdMap = tempBatchToGroupIdMap
                };

                lastJob = writeGroupJob.Schedule(currentGroupLodInfo.MapBatchShape.ToSize(), 32, lastJob);
                var writeEdgeJob = new WriteEdgeJob()
                {
                    TempCrossBatchGroup = tempCrossBatchGroup,
                    TempGroupEdgeGroup = tempGroupEdgeGroup,
                    EdgeMap = EdgeMap.AsParallelWriter(),
                    ParentToChildMapEdge = ParentToChildMapEdge.AsParallelWriter(),
                    TempCombineGroupIdMap = tempCombineGroupInfoMap,
                    GroupInfoMap = GroupInfoMap
                };
                lastJob = writeEdgeJob.Schedule(lastJob);

                var clearDataJob = new ClearDataJob()
                {
                    TempCombineGroupInfoMap = tempCombineGroupInfoMap,
                    TempBatchToGroupIdMap = tempBatchToGroupIdMap,
                    TempCrossBatchGroup = tempCrossBatchGroup,
                    TempGroupEdgeGroup = tempGroupEdgeGroup
                };
                lastJob = clearDataJob.Schedule(lastJob);
            }


            lastJob.Complete();
            tempCombineGroupInfoMap.Dispose();
            tempBatchToGroupIdMap.Dispose();
            tempCrossBatchGroup.Dispose();
            tempGroupEdgeGroup.Dispose();
            IsInit = true;
            return lastJob;
        }
    }
}