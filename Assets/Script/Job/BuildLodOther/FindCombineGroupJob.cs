using System;
using System.Linq;
using Script.PathFind;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Script.Job.BuildLodOther
{
    public struct TempAddEdgeInfo : IEquatable<TempAddEdgeInfo>
    {
        public GroupId SrcGroupId;
        public GroupId DstGroupId;
        public ObstacleType SrcObstacleType;
        public ObstacleType DstObstacleType;

        public TempAddEdgeInfo(GroupId srcGroupId, GroupId dstGroupId,
            ObstacleType srcObstacleType, ObstacleType dstObstacleType)
        {
            SrcGroupId = srcGroupId;
            DstGroupId = dstGroupId;
            SrcObstacleType = srcObstacleType;
            DstObstacleType = dstObstacleType;
        }

        public TempAddEdgeInfo GetTwinEdgeInfo()
        {
            return new TempAddEdgeInfo(DstGroupId, SrcGroupId, DstObstacleType, SrcObstacleType);
        }

        public override string ToString()
        {
            return SrcGroupId + "->" + DstGroupId;
        }

        public bool Equals(TempAddEdgeInfo other)
        {
            return SrcGroupId.Equals(other.SrcGroupId) && DstGroupId.Equals(other.DstGroupId);
        }

        public override bool Equals(object obj)
        {
            return obj is TempAddEdgeInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SrcGroupId, DstGroupId);
        }
    }


    public struct FindCombineGroupJob : IJobParallelFor
    {
        [ReadOnly] public GroupLodInfo CurrentGroupLodInfo;
        [ReadOnly] public GroupLodInfo LastGroupLodInfo;
        [ReadOnly] public NativeParallelHashMap<GroupId, GroupInfo> GroupInfoMap;
        [ReadOnly] public NativeParallelMultiHashMap<GroupId, EdgeInfo> EdgeMap;

        [ReadOnly] public NativeParallelMultiHashMap<GroupId, GroupId> BatchToGroupIdMap;

        // 新的父节点及其子节点列表
        public NativeParallelMultiHashMap<GroupId, GroupId>.ParallelWriter TempCombineGroupInfoMap;

        // 新的批次节点及其包含节点列表
        public NativeParallelMultiHashMap<GroupId, GroupInfo>.ParallelWriter TempBatchToGroupIdMap;

        // 跨越批次的边集合
        public NativeParallelMultiHashMap<GroupId, GroupId>.ParallelWriter TempCrossBatchGroup;

        // 同batch下组的边集合 
        public NativeParallelMultiHashMap<GroupId, GroupId>.ParallelWriter TempGroupEdgeGroup;

        public void Execute(int mapBatchIndex)
        {
            var currentBatchCoord = CurrentGroupLodInfo.GetMapBatchCoordByMapBatchIndex(mapBatchIndex);
            var currentMapChunkCoord = CurrentGroupLodInfo.GetMapChunkCoordByMapBatchCoord(currentBatchCoord);
            var currentChunkBatchCoord = CurrentGroupLodInfo.GetChunkBatchCoordByMapBatchCoord(currentBatchCoord);
            // 当前节点的所有子节点
            using var childGroupSet = new NativeHashSet<GroupId>(CurrentGroupLodInfo.BatchCellShape.x * 4, Allocator.Temp);
            // 边缘节点
            using var borderGroupSet = new NativeHashSet<GroupId>(CurrentGroupLodInfo.BatchCellShape.x * 4, Allocator.Temp);
            // 边缘节点合集父节点
            using var borderUnionParentGroupSet = new NativeHashSet<GroupId>(CurrentGroupLodInfo.BatchCellShape.x * 4, Allocator.Temp);
            // 不同障碍物类型的节点
            using var differentObstacleGroupSet = new NativeHashSet<TempAddEdgeInfo>(CurrentGroupLodInfo.BatchCellShape.x * 4, Allocator.Temp);
            // 与相邻子节点节点相连的子节点
            using var crossChildEdge = new NativeHashSet<TempAddEdgeInfo>(CurrentGroupLodInfo.BatchCellShape.x * 4, Allocator.Temp);
            GenBatchIdGroup(currentBatchCoord, childGroupSet);
            // 合并相邻节点
            using var unionChildGroup = UnionChildGroup(childGroupSet, borderGroupSet, differentObstacleGroupSet, crossChildEdge);
            // 首次合并获取子图
            var childGraphs = unionChildGroup.GetChildGraphs();
            FindBorderGroup(childGraphs, borderGroupSet, borderUnionParentGroupSet);
            // 合并中心节点
            using var unionWithCenterCombine = UnionCenterGroup(unionChildGroup, differentObstacleGroupSet, borderUnionParentGroupSet);
            var childGraphsWithCombineCenter = unionWithCenterCombine.GetChildGraphs();
            // 将仅仅与一个边缘节点相连的中心节点与边缘节点合并  
            using NativeHashMap<GroupId, GroupId> needAddToEdgeList =
                GetAttachToBorderGroup(unionWithCenterCombine, childGraphsWithCombineCenter, borderUnionParentGroupSet, differentObstacleGroupSet);
            UnionAttachToBorderGroup(needAddToEdgeList, childGraphsWithCombineCenter, unionChildGroup);
            // 合并后的子图 
            var childGraphsAfterCombine = unionChildGroup.GetChildGraphs();

            GenNewGroup(childGraphsAfterCombine, currentMapChunkCoord, currentChunkBatchCoord, currentBatchCoord);


            // 与相邻子节点节点相连的子节点
            using var crossParentEdge = new NativeHashSet<TempAddEdgeInfo>(CurrentGroupLodInfo.BatchCellShape.x * 4, Allocator.Temp);
            foreach (var tempEdge in crossChildEdge)
            {
                var src = unionChildGroup.Find(tempEdge.SrcGroupId);
                crossParentEdge.Add(new TempAddEdgeInfo(src, tempEdge.DstGroupId, tempEdge.SrcObstacleType, tempEdge.DstObstacleType));
            }

            using var hasAddParent = new NativeHashSet<TempAddEdgeInfo>(16, Allocator.Temp);
            foreach (var crossEdge in crossParentEdge)
            {
                var srcParent = unionChildGroup.Find(crossEdge.SrcGroupId);
                var temp = new TempAddEdgeInfo();
                temp.SrcGroupId = srcParent;
                temp.DstGroupId = crossEdge.DstGroupId;
                temp.SrcObstacleType = crossEdge.SrcObstacleType;
                temp.DstObstacleType = crossEdge.DstObstacleType;
                if (hasAddParent.Contains(temp))
                {
                    continue;
                }

                hasAddParent.Add(temp);
                TempCrossBatchGroup.Add(srcParent, crossEdge.DstGroupId);
            }

            foreach (var differentObs in differentObstacleGroupSet)
            {
                var src = differentObs.SrcGroupId;
                var dst = differentObs.DstGroupId;
                var srcParent = unionChildGroup.Find(src);
                var dstParent = unionChildGroup.Find(dst);
                var tempDiff = new TempAddEdgeInfo(srcParent, dstParent, differentObs.SrcObstacleType, differentObs.DstObstacleType);
                if (srcParent != dstParent && !hasAddParent.Contains(tempDiff))
                {
                    TempGroupEdgeGroup.Add(srcParent, dstParent);
                    hasAddParent.Add(tempDiff);
                }
            }

            foreach (var child in childGraphs)
            {
                child.Value.Dispose();
            }

            childGraphs.Dispose();

            foreach (var child in childGraphsWithCombineCenter)
            {
                child.Value.Dispose();
            }

            childGraphsWithCombineCenter.Dispose();
            foreach (var child in childGraphsAfterCombine)
            {
                child.Value.Dispose();
            }

            childGraphsAfterCombine.Dispose();
        }


        private void GenNewGroup(
            UnsafeHashMap<GroupId, UnsafeHashSet<GroupId>> childGraphsAfterCombine,
            int2 currentMapChunkCoord, int2 currentChunkBatchCoord, int2 mapBatchCoord
        )
        {
            var offset = 0;
            var batchGroupId = GroupHelper.GenGroupId(currentMapChunkCoord, currentChunkBatchCoord, 0, CurrentGroupLodInfo.CurrentLod);

            foreach (var newGroupKv in childGraphsAfterCombine)
            {
                var newGroupId = GroupHelper.GenGroupId(currentMapChunkCoord, currentChunkBatchCoord, offset, CurrentGroupLodInfo.CurrentLod);
                var parentObstacleType = GroupInfoMap[newGroupKv.Key].ObstacleType;
                var newGroup = new GroupInfo
                {
                    GroupId = newGroupId,
                    ObstacleType = parentObstacleType,
                };

                int2 allPos = 0;
                int count = 0;
                foreach (var childBatchGroupId in newGroupKv.Value)
                {
                    TempCombineGroupInfoMap.Add(newGroupId, childBatchGroupId);

                    var childGroupInfo = GroupInfoMap[childBatchGroupId];
                    var batchCellCoord = childGroupInfo.BatchCellCoordPosition;
                    allPos += batchCellCoord.ToInt2();
                    count += childGroupInfo.Size;
                }

                var arrangeCenter = allPos / count;
                var distance = float.MaxValue;
                var centerGroupId = default(GroupId);
                var nextCenter = (float2)(mapBatchCoord / 2) *
                    CurrentGroupLodInfo.BatchCellShape * 2 + CurrentGroupLodInfo.BatchCellShape;
                foreach (var childBatchGroupId in newGroupKv.Value)
                {
                    var childGroup = GroupInfoMap[childBatchGroupId];
                    var tempCenter = childGroup.BatchCellCoordPosition.ToInt2();
                    var dis = math.distance(arrangeCenter, tempCenter);
                    var disNext = math.distance(nextCenter, tempCenter) * 100;
                    var disAll = dis + disNext;
                    if (disAll < distance)
                    {
                        distance = disAll;
                        centerGroupId = childBatchGroupId;
                    }
                }

                newGroup.BatchCellCoordPosition = GroupInfoMap[centerGroupId].BatchCellCoordPosition;
                newGroup.RepresentGroupId = centerGroupId;
                newGroup.Size = count;
                TempBatchToGroupIdMap.Add(batchGroupId, newGroup);
                offset++;
            }
        }


        /// <summary>
        ///  将仅仅与一个边缘节点相连的中心节点与边缘节点合并
        /// </summary>
        /// <param name="needAddToEdgeList"> 可以合并的中心节点 </param>
        /// <param name="childGraphsWithCombineCenter"> 合并后的边列表 </param>
        /// <param name="unionChildGroup"> 未合并并查集 </param>
        private void UnionAttachToBorderGroup(NativeHashMap<GroupId, GroupId> needAddToEdgeList,
            UnsafeHashMap<GroupId, UnsafeHashSet<GroupId>> childGraphsWithCombineCenter, UnionFind unionChildGroup)
        {
            foreach (var pairs in needAddToEdgeList)
            {
                var center = pairs.Key;
                var border = pairs.Value;
                var combine = childGraphsWithCombineCenter[center];
                unionChildGroup.Union(border, center);
                foreach (var child in combine)
                {
                    Debug.Assert(center.IsValid() && child.IsValid(), $"center:{center} child:{child}");
                    unionChildGroup.Union(border, child);
                }
            }
        }


        /// <summary>
        /// 获取仅仅与一个边缘节点相连的中心节点列表
        /// </summary>
        /// <param name="unionWithCenterCombine">合并中心节点后的并查集</param>
        /// <param name="childGraphsWithCombineCenter">子图</param>
        /// <param name="borderUnionParentGroupSet">边界节点</param>
        /// <returns></returns>
        private NativeHashMap<GroupId, GroupId> GetAttachToBorderGroup(UnionFind unionWithCenterCombine,
            UnsafeHashMap<GroupId, UnsafeHashSet<GroupId>> childGraphsWithCombineCenter,
            NativeHashSet<GroupId> borderUnionParentGroupSet, NativeHashSet<TempAddEdgeInfo> differentObstacleGroupSet)
        {
            var needAddToEdgeList = new NativeHashMap<GroupId, GroupId>(10, Allocator.Temp);
            // 检测合并后中心节点是否与少于1个边缘节点相连
            foreach (var childGraph in childGraphsWithCombineCenter)
            {
                var childGraphKey = childGraph.Key;

                if (borderUnionParentGroupSet.Contains(childGraphKey))
                {
                    continue;
                }

                var count = 0;
                GroupId attachGroup = default;
                foreach (var borderParent in borderUnionParentGroupSet)
                {
                    foreach (var differentObstacle in differentObstacleGroupSet)
                    {
                        var src = differentObstacle.SrcGroupId;
                        var dst = differentObstacle.DstGroupId;
                        if (unionWithCenterCombine.Find(src) == childGraphKey &&
                            unionWithCenterCombine.Find(dst) == borderParent)
                        {
                            count++;
                            attachGroup = borderParent;
                            break;
                        }
                    }

                    if (count > 1)
                    {
                        break;
                    }
                }

                if (count <= 1 && attachGroup.IsValid())
                {
                    needAddToEdgeList.Add(childGraphKey, attachGroup);
                }
            }

            return needAddToEdgeList;
        }

        /// <summary>
        /// 合并中心节点
        /// </summary>
        /// <param name="unionChildGroup"></param>
        /// <param name="differentObstacleGroupSet"></param>
        /// <param name="borderUnionParentGroupSet"></param>
        /// <returns></returns>
        private UnionFind UnionCenterGroup(UnionFind unionChildGroup, NativeHashSet<TempAddEdgeInfo> differentObstacleGroupSet,
            NativeHashSet<GroupId> borderUnionParentGroupSet)
        {
            var unionWithCenterCombine = unionChildGroup.Copy();
            foreach (var tempAddEdgeInfo in differentObstacleGroupSet)
            {
                var srcParent = unionChildGroup.Find(tempAddEdgeInfo.SrcGroupId);
                var dstParent = unionChildGroup.Find(tempAddEdgeInfo.DstGroupId);

                if (borderUnionParentGroupSet.Contains(srcParent) ||
                    borderUnionParentGroupSet.Contains(dstParent))
                {
                    continue;
                }

                unionWithCenterCombine.Union(tempAddEdgeInfo.SrcGroupId, tempAddEdgeInfo.DstGroupId);
            }

            return unionWithCenterCombine;
        }

        /// <summary>
        /// 查找边界节点
        /// </summary>
        /// <param name="childGraphs"></param>
        /// <param name="borderGroupSet"></param>
        /// <param name="borderUnionParentGroupSet"></param>
        private void FindBorderGroup(UnsafeHashMap<GroupId, UnsafeHashSet<GroupId>> childGraphs,
            NativeHashSet<GroupId> borderGroupSet, NativeHashSet<GroupId> borderUnionParentGroupSet)
        {
            foreach (var childGraphKv in childGraphs)
            {
                var unionParent = childGraphKv.Key;

                if (borderGroupSet.Contains(unionParent))
                {
                    borderUnionParentGroupSet.Add(unionParent);
                    continue;
                }

                foreach (var unionChild in childGraphKv.Value)
                {
                    if (borderGroupSet.Contains(unionChild))
                    {
                        borderUnionParentGroupSet.Add(unionParent);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 合并相邻节点
        /// </summary>
        /// <param name="childGroupSet"></param>
        /// <param name="borderGroupSet"></param>
        /// <param name="differentObstacleGroupSet"></param>
        /// <param name="crossChildEdge"></param>
        /// <returns></returns>
        private UnionFind UnionChildGroup(NativeHashSet<GroupId> childGroupSet, NativeHashSet<GroupId> borderGroupSet,
            NativeHashSet<TempAddEdgeInfo> differentObstacleGroupSet, NativeHashSet<TempAddEdgeInfo> crossChildEdge)
        {
            var unionChildGroup = new UnionFind(childGroupSet.Count);
            foreach (var childGroup in childGroupSet)
            {
                var srcGroup = GroupInfoMap[childGroup];
                var srcGroupType = srcGroup.ObstacleType;
                foreach (var edge in EdgeMap.GetValuesForKey(childGroup))
                {
                    // 只合并同一层级的节点
                    if (GroupHelper.GetLod(edge.DstGroupId) != LastGroupLodInfo.CurrentLod)
                    {
                        continue;
                    }

                    var dstGroup = GroupInfoMap[edge.DstGroupId];
                    var dstGroupType = dstGroup.ObstacleType;

                    // 如果目标节点不在当前节点集合中，则目标节点为边界节点
                    if (!childGroupSet.Contains(edge.DstGroupId))
                    {
                        var tempCrossEdge = new TempAddEdgeInfo(srcGroup.GroupId, dstGroup.GroupId, srcGroupType, dstGroupType);
                        crossChildEdge.Add(tempCrossEdge);
                        borderGroupSet.Add(edge.SrcGroupId);
                        Debug.Assert(srcGroup.GroupId.IsValid() && dstGroup.GroupId.IsValid(), $"srcGroup.GroupId:{srcGroup.GroupId} dstGroup.GroupId:{dstGroup.GroupId}");
                        unionChildGroup.Union(edge.SrcGroupId, edge.SrcGroupId);
                        continue;
                    }


                    // 如果目标节点与源节点障碍物类型相同，则合并
                    if (srcGroupType == dstGroupType)
                    {
                        Debug.Assert(srcGroup.GroupId.IsValid() && dstGroup.GroupId.IsValid(), $"srcGroup.GroupId:{srcGroup.GroupId} dstGroup.GroupId:{dstGroup.GroupId}");
                        unionChildGroup.Union(childGroup, edge.DstGroupId);
                    }
                    else
                    {
                        // 如果目标节点与源节点障碍物类型不同，则记录
                        TempAddEdgeInfo tempAddEdgeInfo = new TempAddEdgeInfo();
                        tempAddEdgeInfo.SrcGroupId = srcGroup.GroupId;
                        tempAddEdgeInfo.DstGroupId = dstGroup.GroupId;
                        tempAddEdgeInfo.SrcObstacleType = srcGroupType;
                        tempAddEdgeInfo.DstObstacleType = dstGroupType;
                        differentObstacleGroupSet.Add(tempAddEdgeInfo);
                        differentObstacleGroupSet.Add(tempAddEdgeInfo.GetTwinEdgeInfo());

                        Debug.Assert(srcGroup.GroupId.IsValid() && dstGroup.GroupId.IsValid(), $"srcGroup.GroupId:{srcGroup.GroupId} dstGroup.GroupId:{dstGroup.GroupId}");

                        unionChildGroup.Union(srcGroup.GroupId, srcGroup.GroupId);
                        unionChildGroup.Union(dstGroup.GroupId, dstGroup.GroupId);
                    }
                }
            }

            return unionChildGroup;
        }


        /// <summary>
        /// 获取当前节点的所有子节点
        /// </summary>
        /// <param name="currentBatchCoord"></param>
        /// <param name="childGroupSet"></param>
        private void GenBatchIdGroup(int2 currentBatchCoord, NativeHashSet<GroupId> childGroupSet)
        {
            var mapChildBatchCoord1 = currentBatchCoord * 2;
            var mapChildBatchCoord2 = currentBatchCoord * 2 + new int2(1, 0);
            var mapChildBatchCoord3 = currentBatchCoord * 2 + new int2(0, 1);
            var mapChildBatchCoord4 = currentBatchCoord * 2 + new int2(1, 1);
            AddBatchIdGroup(mapChildBatchCoord1, childGroupSet);
            AddBatchIdGroup(mapChildBatchCoord2, childGroupSet);
            AddBatchIdGroup(mapChildBatchCoord3, childGroupSet);
            AddBatchIdGroup(mapChildBatchCoord4, childGroupSet);
        }

        private GroupId GetBatchChildGroup(int2 mapBatchCoord)
        {
            var mapChunkCoord = LastGroupLodInfo.GetMapChunkCoordByMapBatchCoord(mapBatchCoord);
            var chunkBatchCoord = LastGroupLodInfo.GetChunkBatchCoordByMapBatchCoord(mapBatchCoord);
            return GroupHelper.GenGroupId(mapChunkCoord, chunkBatchCoord, 0, LastGroupLodInfo.CurrentLod);
        }

        private void AddBatchIdGroup(int2 mapBatchCoord, NativeHashSet<GroupId> childGroupList)
        {
            var batchGroupId = GetBatchChildGroup(mapBatchCoord);
            foreach (var groupId in BatchToGroupIdMap.GetValuesForKey(batchGroupId))
            {
                childGroupList.Add(groupId);
            }
        }
    }
}