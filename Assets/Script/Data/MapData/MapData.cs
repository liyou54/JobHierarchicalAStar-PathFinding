using System;
using Script.Job.BuildLodOther;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Script.PathFind
{
    public partial struct MapData : IDisposable
    {
        public NativeParallelHashMap<GroupId, GroupInfo> GroupInfoMap;
        public NativeParallelMultiHashMap<GroupId, EdgeInfo> EdgeMap;
        public NativeArray<ObstacleType> ObstacleMap;
        public NativeArray<GroupId> FirstLodGroupIdIndexMap;
        public NativeParallelMultiHashMap<GroupId, GroupId> BatchToGroupIdMap;
        public MapDataInfo MapDataInfo;
        // 用这个代替边进行跨层
        public NativeParallelMultiHashMap<GroupId, EdgeInfo> ParentToChildMapEdge;


        public MapData(MapDataInfo mapDataInfo)
        {
            ObstacleMap = default;
            GroupInfoMap = new NativeParallelHashMap<GroupId, GroupInfo>
                (mapDataInfo.AllGroupShape.ToSize(), Allocator.Persistent);
            EdgeMap = new NativeParallelMultiHashMap<GroupId, EdgeInfo>(
                mapDataInfo.AllGroupShape.ToSize(), Allocator.Persistent);
            FirstLodGroupIdIndexMap =
                new NativeArray<GroupId>(mapDataInfo.AllGroupShape.ToSize(), Allocator.Persistent);
            BatchToGroupIdMap = new NativeParallelMultiHashMap<GroupId, GroupId>(
                mapDataInfo.AllGroupShape.ToSize(), Allocator.Persistent);
            ParentToChildMapEdge = new NativeParallelMultiHashMap<GroupId, EdgeInfo>(
                mapDataInfo.AllGroupShape.ToSize(), Allocator.Persistent);
            MapDataInfo = mapDataInfo;
            IsInit = false;
        }

        public bool IsInit { get; private set; }

        public void Dispose()
        {
            GroupInfoMap.Dispose();
            EdgeMap.Dispose();
            FirstLodGroupIdIndexMap.Dispose();
            BatchToGroupIdMap.Dispose();
            ParentToChildMapEdge.Dispose();
        }
    }
}