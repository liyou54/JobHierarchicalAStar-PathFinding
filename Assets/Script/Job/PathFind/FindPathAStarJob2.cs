using Script.PathFind;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Script.Job.PathFind
{
    public struct FindPathAStarJob2 : IJob
    {
        [ReadOnly] public NativeParallelHashMap<GroupId, GroupInfo> GroupInfoMap;
        [ReadOnly] public NativeParallelMultiHashMap<GroupId, EdgeInfo> EdgeMap;
        [ReadOnly] public NativeArray<GroupId> FirstLodGroupIdIndexMap;
        [ReadOnly] public NativeParallelMultiHashMap<GroupId, GroupId> BatchToGroupIdMap;
        [ReadOnly] public NativeParallelMultiHashMap<GroupId, EdgeInfo> ParentToChildMapEdge;
        [ReadOnly] public MapDataInfo MapDataInfo;
        [ReadOnly] public ObstacleType OwnerObstacleType;
        [ReadOnly] public Position StartPosition;
        [ReadOnly] public Position EndPosition;
        public NativeList<GroupId> PathListRes;

        public FindPathAStarJob2(MapData mapData, ObstacleType ownerObstacleType, NativeList<GroupId> pathListRes,
            Position startPosition, Position endPosition)
        {
            GroupInfoMap = mapData.GroupInfoMap;
            EdgeMap = mapData.EdgeMap;
            FirstLodGroupIdIndexMap = mapData.FirstLodGroupIdIndexMap;
            BatchToGroupIdMap = mapData.BatchToGroupIdMap;
            ParentToChildMapEdge = mapData.ParentToChildMapEdge;
            MapDataInfo = mapData.MapDataInfo;
            OwnerObstacleType = ownerObstacleType;
            StartPosition = startPosition;
            EndPosition = endPosition;
            PathListRes = pathListRes;
        }

        public void FindParentJob(GroupId src, GroupId dst, NativeList<GroupId> srcList, NativeList<GroupId> dstList)
        {
            srcList.Add(src);
            dstList.Add(dst);
            while (src != dst)
            {
                var srcInfo = GroupInfoMap[src];
                src = srcInfo.ParentGroupId;
                var dstInfo = GroupInfoMap[dst];
                dst = dstInfo.ParentGroupId;

                if (src.IsValid() && dst.IsValid())
                {
                    srcList.Add(src);
                    dstList.Add(dst);
                }
            }
        }

        public void Execute()
        {
            var startGroupId = GetGroupIdByPosition(StartPosition);
            var endGroupId = GetGroupIdByPosition(EndPosition);


            if (startGroupId == GroupId.InValid || endGroupId == GroupId.InValid)
            {
                return;
            }

            using var srcList = new NativeList<GroupId>(8, Allocator.Temp);
            using var dstList = new NativeList<GroupId>(8, Allocator.Temp);

            using var openSet = new NativeHeap<GroupFindNode, ComparerGroupFindNode>(Allocator.Temp, 1024);
            using var closeList = new NativeHashSet<GroupId>(1024, Allocator.Temp);
            using var comeFrom = new NativeParallelHashMap<GroupId, GroupId>(1024, Allocator.Temp);
            using var resultPath1 = new NativeList<GroupId>(1024, Allocator.Temp);
            using var resultPath2 = new NativeList<GroupId>(1024, Allocator.Temp);
            using var groupIdToHeapIndex = new NativeHashMap<GroupId, NativeHeapIndex>(1024, Allocator.Temp);

            FindParentJob(startGroupId, endGroupId, srcList, dstList);

            var currentRes = resultPath1;
            var lastRes = resultPath2;
            for (int i = 0; i < srcList.Length; i++)
            {
                lastRes.Clear();
                openSet.Clear();
                closeList.Clear();
                comeFrom.Clear();
                groupIdToHeapIndex.Clear();

                (currentRes, lastRes) = (lastRes, currentRes);
                var index = srcList.Length - i - 1;
                Find(srcList[index], dstList[index], openSet, closeList, comeFrom, groupIdToHeapIndex, currentRes, lastRes);
                if (currentRes.Length == 0)
                {
                    break;
                }
            }
            PathListRes.CopyFrom(currentRes);
        }

        private void Find(GroupId startGroupId, GroupId endGroupId,
            NativeHeap<GroupFindNode, ComparerGroupFindNode> openSet,
            NativeHashSet<GroupId> closeList,
            NativeParallelHashMap<GroupId, GroupId> comeFrom,
            NativeHashMap<GroupId, NativeHeapIndex> groupIdToHeapIndex,
            NativeList<GroupId> resultPath,
            NativeList<GroupId> lastResPath)
        {
            var startGroupInfo = GetGroupInfoByGroupId(startGroupId);

            var startNode = new GroupFindNode
            {
                GroupInfo = startGroupInfo,
                HeuristicCostFromLastStep = 0,
                ActualCostFromLastStep = 0,
                RemainingHeuristicCost = math.distance(StartPosition.ToInt2(), EndPosition.ToInt2()),
                ActualCostUpToLastStep = 0
            };
            var startNodeIndex = openSet.Insert(startNode);
            groupIdToHeapIndex.Add(startGroupId, startNodeIndex);
            while (!IsFinish(openSet, startNode.GroupInfo.GroupId, endGroupId))
            {
                var currentNode = openSet.Pop();
                var currentNodeId = currentNode.GroupInfo.GroupId;
                closeList.Add(currentNode.GroupInfo.GroupId);

                foreach (var edgeInfo in EdgeMap.GetValuesForKey(currentNodeId))
                {
                    var dstGroupId = edgeInfo.DstGroupId;
                    var dstGroupInfo = GroupInfoMap[dstGroupId];

                    if (lastResPath.Length > 0 && !lastResPath.Contains(dstGroupInfo.ParentGroupId))
                    {
                        continue;
                    }

                    if ((OwnerObstacleType & dstGroupInfo.ObstacleType) != dstGroupInfo.ObstacleType)
                    {
                        continue;
                    }

                    var finish = FindStepAndCheckFinish(currentNode, dstGroupInfo, endGroupId, openSet, closeList, comeFrom,
                        resultPath, groupIdToHeapIndex);
                    if (finish)
                    {
                        break;
                    }
                }
            }

            var currentGroupId = endGroupId;
            if (comeFrom.TryGetValue(endGroupId, out var _))
            {
                resultPath.Add(endGroupId);
            }
            while (comeFrom.TryGetValue(currentGroupId, out var parentId))
            {
                resultPath.Add(parentId);
                currentGroupId = parentId;
            }
        }

        private bool FindStepAndCheckFinish(GroupFindNode lastNode, GroupInfo dstGroupInfo, GroupId endGroupId,
            NativeHeap<GroupFindNode, ComparerGroupFindNode> openSet,
            NativeHashSet<GroupId> closeList,
            NativeParallelHashMap<GroupId, GroupId> comeFrom,
            NativeList<GroupId> resultPath,
            NativeHashMap<GroupId, NativeHeapIndex> groupIdToHeapIndex)
        {
            var dstId = dstGroupInfo.GroupId;

            if (closeList.Contains(dstId))
            {
                return false;
            }

            bool hasAdd = groupIdToHeapIndex.TryGetValue(dstId, out var heapIndex);
            hasAdd = hasAdd && openSet.IsValidIndex(heapIndex);

            var srcInfo = lastNode.GroupInfo;
            var srcPos = srcInfo.BatchCellCoordPosition;
            var dstPos = GroupInfoMap[dstGroupInfo.GroupId].BatchCellCoordPosition;
            var thisStepR = math.distance(srcPos.ToInt2(), dstPos.ToInt2());
            var thisStepH = thisStepR * .99f;
            var remainH = math.distance(dstPos.ToInt2(), EndPosition.ToInt2());
            var actualCost = lastNode.GetActualCost();
            var node = new GroupFindNode();
            node.GroupInfo = GroupInfoMap[dstId];
            node.HeuristicCostFromLastStep = thisStepH;
            node.ActualCostFromLastStep = thisStepR;
            node.RemainingHeuristicCost = remainH;
            node.ActualCostUpToLastStep = actualCost;

            if (!hasAdd)
            {
                var index = openSet.Insert(node);
                groupIdToHeapIndex.Add(dstId, index);
                comeFrom.Add(dstId, lastNode.GroupInfo.GroupId);
                if (IsFinish(openSet, dstId, endGroupId))
                {
                    return true;
                }
            }
            else
            {
                var openData = openSet[heapIndex];
                if (node.GetHeuristicCost() < openData.GetHeuristicCost())
                {
                    openSet.Remove(heapIndex);
                    var newIndex = openSet.Insert(openData);
                    groupIdToHeapIndex[dstId] = newIndex;
                    comeFrom[dstId] = lastNode.GroupInfo.GroupId;
                }
            }

            return false;
        }


        private bool IsFinish(NativeHeap<GroupFindNode, ComparerGroupFindNode> openSet, GroupId start, GroupId end)
        {
            var isFinish = start == end;
            var isFail = openSet.Count == 0;

            return isFinish || isFail;
        }

        private int GetMapCellIndexByPosition(Position position)
        {
            return position.y * MapDataInfo.AllGroupShape.x + position.x;
        }

        private GroupId GetGroupIdByPosition(Position start)
        {
            var index = GetMapCellIndexByPosition(start);
            return FirstLodGroupIdIndexMap[index];
        }

        private GroupInfo GetGroupInfoByGroupId(GroupId groupId)
        {
            return GroupInfoMap[groupId];
        }
    }
}