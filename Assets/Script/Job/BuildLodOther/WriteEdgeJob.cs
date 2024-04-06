using Script.PathFind;
using Unity.Collections;
using Unity.Jobs;

namespace Script.Job.BuildLodOther
{
    public struct WriteEdgeJob : IJob
    {
        // 跨越批次的边集合
        public NativeParallelMultiHashMap<GroupId, GroupId> TempCrossBatchGroup;
        public NativeParallelMultiHashMap<GroupId, GroupId> TempCombineGroupIdMap;
        public NativeParallelMultiHashMap<GroupId, EdgeInfo>.ParallelWriter ParentToChildMapEdge;

        // 同batch下组的边集合 已经去重了
        public NativeParallelMultiHashMap<GroupId, GroupId> TempGroupEdgeGroup;
        public NativeParallelMultiHashMap<GroupId, EdgeInfo>.ParallelWriter EdgeMap;
        public NativeParallelHashMap<GroupId, GroupInfo> GroupInfoMap;

        public void Execute()
        {
            using var tempEdgeHash = new NativeHashSet<EdgeInfo>(TempCrossBatchGroup.Count() * 2, Allocator.Temp);
            using var tempEdgeHash2 = new NativeHashSet<EdgeInfo>(TempCombineGroupIdMap.Count(), Allocator.Temp);

            foreach (var kv in TempCombineGroupIdMap)
            {
                var edgeInfo = new EdgeInfo
                {
                    SrcGroupId = kv.Key,
                    DstGroupId = kv.Value,
                    ObstacleType = GroupInfoMap[kv.Value].ObstacleType
                };

                if (!tempEdgeHash2.Contains(edgeInfo))
                {
                    tempEdgeHash2.Add(edgeInfo);
                    ParentToChildMapEdge.Add(kv.Key, edgeInfo);
                }
            }
            
            foreach (var tempEdge in TempCrossBatchGroup)
            {
                var src = tempEdge.Key;
                var srcInfo = GroupInfoMap[src];
                var dst = GroupInfoMap[tempEdge.Value];
                var edge = new EdgeInfo { SrcGroupId = srcInfo.ParentGroupId, DstGroupId = dst.ParentGroupId, ObstacleType = dst.ObstacleType };
                if (!tempEdgeHash.Contains(edge))
                {
                    tempEdgeHash.Add(edge);
                    EdgeMap.Add(srcInfo.ParentGroupId, edge);
                }
            }

            foreach (var tempEdge in TempGroupEdgeGroup)
            {
                var src = tempEdge.Key;
                var srcInfo = GroupInfoMap[src];
                var dstInfo = GroupInfoMap[tempEdge.Value];
                EdgeMap.Add(srcInfo.ParentGroupId, new EdgeInfo { SrcGroupId = srcInfo.ParentGroupId, DstGroupId = dstInfo.ParentGroupId, ObstacleType = dstInfo.ObstacleType });
            }
        }
    }
}