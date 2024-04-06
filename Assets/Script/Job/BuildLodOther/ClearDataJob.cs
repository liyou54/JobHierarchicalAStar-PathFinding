using Unity.Collections;
using Unity.Jobs;

namespace Script.PathFind
{
    public struct ClearDataJob:IJob
    {
        // 新的父节点及其子节点列表
        public NativeParallelMultiHashMap<GroupId, GroupId> TempCombineGroupInfoMap;

        // 新的批次节点及其包含节点列表
        public NativeParallelMultiHashMap<GroupId, GroupInfo> TempBatchToGroupIdMap;

        // 跨越批次的边集合
        public NativeParallelMultiHashMap<GroupId, GroupId> TempCrossBatchGroup;

        // 同batch下组的边集合
        public NativeParallelMultiHashMap<GroupId, GroupId> TempGroupEdgeGroup;


        public void Execute()
        {
            TempCombineGroupInfoMap.Clear();
            TempBatchToGroupIdMap.Clear();
            TempCrossBatchGroup.Clear();
            TempGroupEdgeGroup.Clear();
        }
    }
}