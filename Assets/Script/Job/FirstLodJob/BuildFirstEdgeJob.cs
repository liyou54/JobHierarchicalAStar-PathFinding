using Script.PathFind;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public struct BuildFirstEdgeMap : IJobParallelFor
    {
        public NativeParallelMultiHashMap<GroupId, EdgeInfo>.ParallelWriter EdgeMap;
        [ReadOnly] public GroupLodInfo GroupLodInfo;
        [FormerlySerializedAs("FirstLevelGroupIdMap")] [ReadOnly] public NativeArray<GroupId> FirstLodGroupIdIndexMap;
        [ReadOnly] public NativeParallelHashMap<GroupId, GroupInfo> GroupMap;

        public void Execute(int mapBatchId)
        {
            var batchCellSize = GroupLodInfo.BatchCellShape.x;
            var mapBatchCoord = GroupLodInfo.GetMapBatchCoordByMapBatchIndex(mapBatchId);
            using var edgeHash = new NativeHashSet<EdgeInfo>(batchCellSize * batchCellSize, Allocator.Temp);
            if (mapBatchCoord.x > 0)
            {
                var leftMapBatchId = new int2(mapBatchCoord.x - 1, mapBatchCoord.y);
                for (int i = 0; i < batchCellSize; i++)
                {
                    var cellIndex = GroupLodInfo.GetMapCellIndexByMapBatchCoordAndOffset(mapBatchCoord, new int2(0, i));
                    var leftCellIndex = GroupLodInfo.GetMapCellIndexByMapBatchCoordAndOffset(leftMapBatchId, new int2(batchCellSize - 1, i));
                    if (FirstLodGroupIdIndexMap[cellIndex].IsValid() && FirstLodGroupIdIndexMap[leftCellIndex].IsValid())
                    {
                        var srcGroup = FirstLodGroupIdIndexMap[cellIndex];
                        var dstGroup = FirstLodGroupIdIndexMap[leftCellIndex];
                        var srcType = GroupMap[srcGroup].ObstacleType;
                        var dstType = GroupMap[dstGroup].ObstacleType;

                        var edgeInfo = new EdgeInfo(srcGroup, dstGroup, dstType);
                        var edgeInfo2 = new EdgeInfo(dstGroup, srcGroup, srcType);
                        edgeHash.Add(edgeInfo);
                        edgeHash.Add(edgeInfo2);
                    }
                }
            }

            if (mapBatchCoord.y > 0)
            {
                var downBatchId = new int2(mapBatchCoord.x, mapBatchCoord.y - 1);
                for (int i = 0; i < batchCellSize; i++)
                {
                    var cellIndex = GroupLodInfo.GetMapCellIndexByMapBatchCoordAndOffset(mapBatchCoord, new int2(i, 0));
                    var downCellIndex = GroupLodInfo.GetMapCellIndexByMapBatchCoordAndOffset(downBatchId, new int2(i, batchCellSize - 1));
                    if (FirstLodGroupIdIndexMap[cellIndex] != -1 && FirstLodGroupIdIndexMap[downCellIndex] != -1)
                    {
                        var srcGroup = FirstLodGroupIdIndexMap[cellIndex];
                        var dstGroup = FirstLodGroupIdIndexMap[downCellIndex];
                        if (srcGroup.IsValid() && dstGroup.IsValid())
                        {
                            var srcType = GroupMap[srcGroup].ObstacleType;
                            var dstType = GroupMap[dstGroup].ObstacleType;
                            var edgeInfo = new EdgeInfo(srcGroup, dstGroup, dstType);
                            var edgeInfo2 = new EdgeInfo(dstGroup, srcGroup, srcType);
                            edgeHash.Add(edgeInfo);
                            edgeHash.Add(edgeInfo2);
                        }
                    }
                }
            }

            foreach (var edge in edgeHash)
            {
                EdgeMap.Add(edge.SrcGroupId, edge);
            }
        }
    }
