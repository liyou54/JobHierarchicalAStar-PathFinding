using Script.PathFind;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Script.Job.BuildLodOther
{
    public struct SetLastGroupParentJob : IJobParallelFor
    {
        [ReadOnly] public GroupLodInfo CurrentGroupLodInfo;

        // 新的父节点及其子节点列表
        [ReadOnly] public NativeParallelMultiHashMap<GroupId, GroupId> TempCombineGroupIdMap;

        // 新的批次节点及其包含节点列表
        [ReadOnly] public NativeParallelMultiHashMap<GroupId, GroupInfo> TempBatchToGroupIdMap;

        [NativeDisableContainerSafetyRestriction]
        public NativeParallelHashMap<GroupId, GroupInfo> GroupInfoMap;

        public NativeParallelMultiHashMap<GroupId, GroupId>.ParallelWriter BatchToGroupIdMap;


        public void Execute(int mapBatchId)
        {
            var mapBatchCoord = CurrentGroupLodInfo.GetMapBatchCoordByMapBatchIndex(mapBatchId);
            var mapChunkCoord = CurrentGroupLodInfo.GetMapChunkCoordByMapBatchCoord(mapBatchCoord);
            var chunkBatchCoord = CurrentGroupLodInfo.GetChunkBatchCoordByMapBatchCoord(mapBatchCoord);
            var newBatchInfo = GroupHelper.GenGroupId(mapChunkCoord, chunkBatchCoord, 0, CurrentGroupLodInfo.CurrentLod);

            foreach (var newGroup in TempBatchToGroupIdMap.GetValuesForKey(newBatchInfo))
            {
                foreach (var child in TempCombineGroupIdMap.GetValuesForKey(newGroup.GroupId))
                {
                    var childGroupInfo = GroupInfoMap[child];
                    childGroupInfo.ParentGroupId = newGroup.GroupId;
                    GroupInfoMap[child] = childGroupInfo;
                }
            }
        }
    }
}