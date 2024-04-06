using Script.PathFind;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Script.Job.BuildLodOther
{
    public struct WriteGroupJob : IJobParallelFor
    {
        [ReadOnly] public GroupLodInfo CurrentGroupLodInfo;
        public NativeParallelHashMap<GroupId, GroupInfo>.ParallelWriter GroupInfoMap;
        public NativeParallelMultiHashMap<GroupId, GroupId>.ParallelWriter BatchToGroupIdMap;

        // 新的批次节点及其包含节点列表
        [ReadOnly] public NativeParallelMultiHashMap<GroupId, GroupInfo> TempBatchToGroupIdMap;


        public void Execute(int mapBatchId)
        {
            var mapBatchCoord = CurrentGroupLodInfo.GetMapBatchCoordByMapBatchIndex(mapBatchId);
            var mapChunkCoord = CurrentGroupLodInfo.GetMapChunkCoordByMapBatchCoord(mapBatchCoord);
            var chunkBatchCoord = CurrentGroupLodInfo.GetChunkBatchCoordByMapBatchCoord(mapBatchCoord);
            var newBatchInfo = GroupHelper.GenGroupId(mapChunkCoord, chunkBatchCoord, 0, CurrentGroupLodInfo.CurrentLod);

            foreach (var newGroup in TempBatchToGroupIdMap.GetValuesForKey(newBatchInfo))
            {
                GroupInfoMap.TryAdd(newGroup.GroupId, newGroup);
                BatchToGroupIdMap.Add(newBatchInfo, newGroup.GroupId);
            }
        }
    }
}