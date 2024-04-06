using Script.PathFind;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

#if false

namespace Script.Job.PathFind
{
    public struct FindPathAStarJob : IJob
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
        [FormerlySerializedAs("OpenList")] public NativeHeap<GroupFindNode, ComparerGroupFindNode> OpenSet;
        public NativeHashSet<GroupId> CloseList;
        public NativeParallelHashMap<GroupId, GroupId> ComeFrom;
        public NativeList<GroupId> ResultPath;
        public NativeHashMap<GroupId, NativeHeapIndex> GroupIdToHeapIndex;
        public int MaxPathLength;
        public int MaxOpenListLength;

        public FindPathAStarJob(MapData mapData, ObstacleType ownerObstacleType,
            Position startPosition, Position endPosition,
            int maxPathLength = 1024, int maxOpenListLength = 4096)
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
            MaxPathLength = maxPathLength;
            MaxOpenListLength = maxOpenListLength;
            OpenSet = new NativeHeap<GroupFindNode, ComparerGroupFindNode>(Allocator.TempJob, MaxOpenListLength);
            CloseList = new NativeHashSet<GroupId>(MaxOpenListLength, Allocator.TempJob);
            ComeFrom = new NativeParallelHashMap<GroupId, GroupId>(MaxOpenListLength, Allocator.TempJob);
            ResultPath = new NativeList<GroupId>(MaxPathLength, Allocator.TempJob);
            GroupIdToHeapIndex = new NativeHashMap<GroupId, NativeHeapIndex>(MaxOpenListLength, Allocator.TempJob);
        }

        public void ResetJob()
        {
            OpenSet.Clear();
            CloseList.Clear();
            ComeFrom.Clear();
        }

        public void Execute()
        {
            var startGroupId = GetGroupIdByPosition(StartPosition);
            var endGroupId = GetGroupIdByPosition(EndPosition);
            if (startGroupId == GroupId.InValid || endGroupId == GroupId.InValid)
            {
                return;
            }

            Find(startGroupId, endGroupId);

            if (!ComeFrom.ContainsKey(endGroupId))
            {
                return;
            }
        }

        public void Find(GroupId startGroupId, GroupId endGroupId)
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

            var heapIndex = OpenSet.Insert(startNode);
            GroupIdToHeapIndex.Add(startGroupId, heapIndex);

            while (!IsFinish(startNode.GroupInfo.GroupId, endGroupId))
            {
                var currentNode = OpenSet.Pop();
                var currentNodeId = currentNode.GroupInfo.GroupId;
                CloseList.Add(currentNode.GroupInfo.GroupId);
                // 判断是否需要改变层次
                var changeLod = GetChangeLod(currentNode);
                if (changeLod == ChangeLod.Down)
                {
                    foreach (var downEdge in ParentToChildMapEdge.GetValuesForKey(currentNodeId))
                    {
                        if (FindStepAndCheckFinish(currentNode, downEdge.DstGroupId, endGroupId))
                        {
                            return;
                        }
                    }
                }
                else
                {
                    foreach (var edge in EdgeMap.GetValuesForKey(currentNodeId))
                    {
                        if (FindStepAndCheckFinish(currentNode, edge.DstGroupId, endGroupId))
                        {
                            return;
                        }
                    }

                    if (currentNode.GroupInfo.ParentGroupId.IsValid() && FindStepAndCheckFinish(currentNode, currentNode.GroupInfo.ParentGroupId, endGroupId))
                    {
                        return;
                    }
                }
            }
        }

        private bool FindStepAndCheckFinish(GroupFindNode lastNode, GroupId dst, GroupId endGroupId)
        {
            if (CloseList.Contains(dst))
            {
                return false;
            }

            bool hasAdd = GroupIdToHeapIndex.TryGetValue(dst, out var heapIndex);
            hasAdd = hasAdd && OpenSet.IsValidIndex(heapIndex);

            var srcInfo = lastNode.GroupInfo;
            var dstInfo = GroupInfoMap[dst];
            var srcPos = srcInfo.BatchCellCoordPosition;
            var dstPos = GroupInfoMap[dstInfo.GroupId].BatchCellCoordPosition;
            var thisStepR = math.distance(srcPos.ToInt2(), dstPos.ToInt2());
            var thisStepH = thisStepR * .99f;
            var remainH = math.distance(dstPos.ToInt2(), EndPosition.ToInt2());
            var actualCost = lastNode.GetActualCost();
            var node = new GroupFindNode();
            node.GroupInfo = GroupInfoMap[dstInfo.GroupId];
            node.HeuristicCostFromLastStep = thisStepH;
            node.ActualCostFromLastStep = thisStepR;
            node.RemainingHeuristicCost = remainH;
            node.ActualCostUpToLastStep = actualCost;

            if (!hasAdd)
            {
                var index = OpenSet.Insert(node);
                GroupIdToHeapIndex.Add(dst, index);
                if (IsFinish(dst, endGroupId))
                {
                    ResultPath.Add(dst);
                    ComeFrom.Add(dst, lastNode.GroupInfo.GroupId);
                    return true;
                }
            }
            else
            {
                var openData = OpenSet[heapIndex];
                if (node.GetHeuristicCost() < openData.GetHeuristicCost())
                {
                    OpenSet.Remove(heapIndex);
                    var newIndex = OpenSet.Insert(openData);
                    GroupIdToHeapIndex[dst] = newIndex;
                    ComeFrom[dst] = lastNode.GroupInfo.GroupId;
                }
            }

            return false;
        }

        private enum ChangeLod
        {
            Up,
            Down,
            Keep
        }


        private ChangeLod GetChangeLod(GroupFindNode findNode)
        {
            var groupId = findNode.GroupInfo.GroupId;
            var lod = GroupHelper.GetLod(groupId);
            var size = groupId.GetBatchCellSize();
            var costUp = (MapDataInfo.MaxLod - lod) * size;
            if (findNode.RemainingHeuristicCost < costUp && lod > MapDataInfo.StartLod)
            {
                return ChangeLod.Down;
            }

            if (findNode.GetActualCost() > costUp && lod < MapDataInfo.MaxLod)
            {
                return ChangeLod.Up;
            }

            return ChangeLod.Keep;
        }

        private bool IsFinish(GroupId start, GroupId end)
        {
            var isFullCloseList = CloseList.Count == MaxPathLength;
            var isFullOpenSet = OpenSet.Count == MaxOpenListLength;
            var isFullComeFrom = ComeFrom.Count() == MaxOpenListLength;
            var isFullResultPath = ResultPath.Length == MaxPathLength;
            var isFullGroupIdToHeap = GroupIdToHeapIndex.Count == MaxOpenListLength;
            
#if UNITY_EDITOR
            Debug.Assert(!isFullCloseList, "CloseList is full");
            Debug.Assert(!isFullOpenSet, "OpenSet is full");
            Debug.Assert(!isFullComeFrom, "ComeFrom is full");
            Debug.Assert(!isFullResultPath, "ResultPath is full");
            Debug.Assert(!isFullGroupIdToHeap, "GroupIdToHeapIndex is full");
#endif
            var isFinish = start == end;
            var isFail = OpenSet.Count == 0;
            // return isFinish || isFail || isFullTempCollection;
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

#endif