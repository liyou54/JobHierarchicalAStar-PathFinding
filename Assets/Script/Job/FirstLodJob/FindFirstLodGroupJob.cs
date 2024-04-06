using System;
using Script.PathFind;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
internal struct FindFirstLodGroupJob : IJobParallelFor
{
    [ReadOnly] public GroupLodInfo GroupLodInfo;

    public NativeParallelHashMap<GroupId, GroupInfo>.ParallelWriter GroupInfoMap;
    public NativeParallelMultiHashMap<GroupId, EdgeInfo>.ParallelWriter EdgeMap;
    [ReadOnly] public NativeArray<ObstacleType> ObstacleMap;

    [NativeDisableContainerSafetyRestriction]
    public NativeArray<GroupId> FirstLodGroupIdIndexMap;

    public NativeParallelMultiHashMap<GroupId, GroupId>.ParallelWriter BatchToGroupIdMap;


    [BurstCompile]
    public void Execute(int mapBatchId)
    {
        var mapBatchCoord = GroupLodInfo.GetMapBatchCoordByMapBatchIndex(mapBatchId);
        using var visit = new NativeArray<bool>(GroupLodInfo.BatchCellShape.ToSize(), Allocator.Temp);
        var batchAllCount = GroupLodInfo.BatchCellShape.x * GroupLodInfo.BatchCellShape.y;
        using var tempGroupMapPos =
            new NativeParallelHashMap<int, GroupId>(batchAllCount,
                Allocator.Temp);
        using var tempGroupMapInfo = new NativeParallelHashMap<GroupId, TempGroupInfo>(batchAllCount, Allocator.Temp);
        var tempGroupMapPosWriter = tempGroupMapPos.AsParallelWriter();
        var tempGroupMapInfoWriter = tempGroupMapInfo.AsParallelWriter();
        int2 mapChunkCoord = GroupLodInfo.GetMapChunkCoordByMapBatchCoord(mapBatchCoord);
        int2 chunkBatchCoord = GroupLodInfo.GetChunkBatchCoordByMapBatchCoord(mapBatchCoord);
        var tempOffset = 0;
        while (FindUnFinish(visit, out var batchCellOffsetCoord))
        {
            TempGroupInfo tempGroupInfo = default;
            var fillType = GetObstacleType(mapBatchCoord, batchCellOffsetCoord);
            tempGroupInfo.ObstacleType = fillType;
            tempGroupInfo.CurrentDistance = Single.MaxValue;

            tempGroupInfo.GroupId = GroupHelper.GenGroupId(mapChunkCoord, chunkBatchCoord, tempOffset, GroupLodInfo.MapDataInfo.StartLod);
            if (fillType == ObstacleType.Hard)
            {
                tempGroupInfo.GroupId = GroupId.InValid;
            }

            FillStep(mapBatchCoord, batchCellOffsetCoord, ref tempGroupInfo, visit, tempGroupMapPosWriter);
            tempGroupMapInfoWriter.TryAdd(tempGroupInfo.GroupId, tempGroupInfo);
            tempOffset++;
        }

        FillGroupIdMap(mapBatchCoord, tempGroupMapPos, tempGroupMapInfo);
        FillEdgeMap(tempGroupMapInfo, tempGroupMapPos);
    }

    private void FillEdgeMap(NativeParallelHashMap<GroupId, TempGroupInfo> tempGroupMapInfo, NativeParallelHashMap<int, GroupId> tempGroupMapPos)
    {
        bool TryFind(int x, int y, int fillSize, TempGroupInfo currentGroupId, out EdgeInfo value)
        {
            value = default;
            if (x < 0 || x >= fillSize || y < 0 || y >= fillSize || currentGroupId.ObstacleType == ObstacleType.Hard)
            {
                return false;
            }

            var nextGroupId = tempGroupMapPos[y * fillSize + x];
            var nextGroup = tempGroupMapInfo[nextGroupId];

            if (nextGroup.ObstacleType == ObstacleType.Hard)
            {
                return false;
            }

            if (nextGroup.GroupId == currentGroupId.GroupId)
            {
                return false;
            }

            value = new EdgeInfo(currentGroupId.GroupId, nextGroupId, nextGroup.ObstacleType);

            return true;
        }

        var batchSize = GroupLodInfo.BatchCellShape.x;
        using NativeHashSet<EdgeInfo> edgeMap = new NativeHashSet<EdgeInfo>(batchSize * batchSize, Allocator.Temp);
        for (int y = 0; y < batchSize; y++)
        {
            for (int x = 0; x < batchSize; x++)
            {
                var index = GroupLodInfo.GetBatchCellIndexByBatchCellCoord(new int2(x, y));
                var groupId = tempGroupMapPos[index];
                var groupInfo = tempGroupMapInfo[groupId];
                if (TryFind(x - 1, y, batchSize, groupInfo, out var value))
                {
                    edgeMap.Add(value);
                }

                if (TryFind(x + 1, y, batchSize, groupInfo, out value))
                {
                    edgeMap.Add(value);
                }

                if (TryFind(x, y - 1, batchSize, groupInfo, out value))
                {
                    edgeMap.Add(value);
                }

                if (TryFind(x, y + 1, batchSize, groupInfo, out value))
                {
                    edgeMap.Add(value);
                }
            }
        }

        foreach (var edgeInfo in edgeMap)
        {
            EdgeMap.Add(edgeInfo.SrcGroupId, edgeInfo);
        }
    }

    private void FillGroupIdMap(int2 mapBatchCoord,
        NativeParallelHashMap<int, GroupId> tempGroupMapPos,
        NativeParallelHashMap<GroupId, TempGroupInfo> tempGroupMapInfo)
    {
        foreach (var group in tempGroupMapPos)
        {
            var batchCellCoord = GroupLodInfo.GetBatchCellCoordByIndex(group.Key);
            var mapCellCoord = GroupLodInfo.GetMapCellCoordByMapBatchCoordAndOffset(mapBatchCoord, batchCellCoord);
            var mapCellId = GroupLodInfo.MapCellCoordToIndex(mapCellCoord);
            FirstLodGroupIdIndexMap[mapCellId] = group.Value;
            var temp = tempGroupMapInfo[group.Value];
            var distance = math.distance(batchCellCoord, temp.PosAll / temp.Size);
            if (temp.CurrentDistance > distance)
            {
                temp.CurrentDistance = distance;
                temp.CenterPoint =  mapCellCoord;
            }

            tempGroupMapInfo[group.Value] = temp;
        }

        foreach (var group in tempGroupMapInfo)
        {
            var index = GroupHelper.RemoveOffset(group.Key);

            if (group.Value.ObstacleType != ObstacleType.Hard)
            {
                BatchToGroupIdMap.Add(index, group.Key);
                var pos = GroupHelper.Int2ToPosition(group.Value.CenterPoint);
                GroupInfoMap.TryAdd(group.Key, new GroupInfo
                {
                    GroupId = group.Key,
                    ObstacleType = group.Value.ObstacleType,
                    ParentGroupId = GroupId.InValid,
                    BatchCellCoordPosition = pos,
                    RepresentGroupId = group.Key,
                    Size = group.Value.Size
                });
            }
        }
    }

    [BurstCompile]
    private void FillStep(int2 mapBatchCoord, int2 batchCellCoord, ref TempGroupInfo tempGroupInfo, NativeArray<bool> visit,
        NativeParallelHashMap<int, GroupId>.ParallelWriter tempGroupIdPos)
    {
        Dfs(mapBatchCoord, batchCellCoord, ref tempGroupInfo, visit, tempGroupIdPos);
    }

    private void Dfs(int2 mapBatchCoord, int2 batchCellCoord,
        ref TempGroupInfo tempGroupInfo, NativeArray<bool> visit,
        NativeParallelHashMap<int, GroupId>.ParallelWriter tempGroupIdPos)
    {
        var batchCellIndex = GroupLodInfo.GetBatchCellIndexByBatchCellCoord(batchCellCoord);

        if (batchCellCoord.x < 0 || batchCellCoord.x >= GroupLodInfo.BatchCellShape.x ||
            batchCellCoord.y < 0 || batchCellCoord.y >= GroupLodInfo.BatchCellShape.y || visit[batchCellIndex])
        {
            return;
        }

        var obstacleType = GetObstacleType(mapBatchCoord, batchCellCoord);
        if (obstacleType != tempGroupInfo.ObstacleType)
        {
            return;
        }

        visit[batchCellIndex] = true;
        tempGroupInfo.Size++;
        tempGroupInfo.PosAll += (batchCellCoord);
        tempGroupIdPos.TryAdd(batchCellIndex, tempGroupInfo.GroupId);

        var batchOffsetPos1 = batchCellCoord + new int2(0, -1);
        Dfs(mapBatchCoord, batchOffsetPos1, ref tempGroupInfo, visit, tempGroupIdPos); // 下
        var batchOffsetPos2 = batchCellCoord + new int2(0, 1);
        Dfs(mapBatchCoord, batchOffsetPos2, ref tempGroupInfo, visit, tempGroupIdPos); // 上
        var batchOffsetPos3 = batchCellCoord + new int2(-1, 0);
        Dfs(mapBatchCoord, batchOffsetPos3, ref tempGroupInfo, visit, tempGroupIdPos); // 左
        var batchOffsetPos4 = batchCellCoord + new int2(1, 0);
        Dfs(mapBatchCoord, batchOffsetPos4, ref tempGroupInfo, visit, tempGroupIdPos); // 右
    }

    private bool FindUnFinish(NativeArray<bool> visit, out int2 batchCellCoord)
    {
        batchCellCoord = 0;
        for (int i = 0; i < visit.Length; i++)
        {
            if (!visit[i])
            {
                batchCellCoord = GroupLodInfo.GetBatchCellCoordByIndex(i);
                return true;
            }
        }

        return false;
    }

    private ObstacleType GetObstacleType(int2 batchCoord, int2 batchCellOffsetCoord)
    {
        var mapCellCoord = GroupLodInfo.GetMapCellCoordByMapBatchCoordAndOffset(batchCoord, batchCellOffsetCoord);

        if (mapCellCoord.x < 0 || mapCellCoord.y < 0 || 
            mapCellCoord.x >= GroupLodInfo.MapDataInfo.ObstacleShape.x ||
            mapCellCoord.y >=  GroupLodInfo.MapDataInfo.ObstacleShape.y)
        {
            return ObstacleType.Hard;
        }

        var mapCellId = GroupLodInfo.MapCellCoordToObstacleIndex(mapCellCoord);
        return ObstacleMap[mapCellId];
    }

    private struct TempGroupInfo
    {
        public int Offset;
        public ObstacleType ObstacleType;
        public GroupId GroupId;
        public int Size;
        public int2 PosAll;

        public int2 CenterPoint;
        public float CurrentDistance;
    }
}