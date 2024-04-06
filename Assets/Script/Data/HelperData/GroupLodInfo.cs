using Unity.Mathematics;

namespace Script.PathFind
{
    
    
    public struct GroupLodInfo
    {
        public int2 MapCellShape;
        public int2 MapChunkShape;
        public int2 MapBatchShape;
        public int2 ChunkCellShape;
        public int2 ChunkBatchShape;
        public int2 BatchCellShape;
        public MapDataInfo MapDataInfo;
        public int CurrentLod;
        public GroupLodInfo(MapDataInfo mapDataInfo,int currentLod)
        {
            MapDataInfo = mapDataInfo;
            CurrentLod = currentLod;
            MapCellShape = MapDataInfo.AllGroupShape;
            BatchCellShape = new int2(1 << currentLod, 1 << currentLod);
            MapChunkShape = MapDataInfo.AllGroupShape / BatchCellShape;
            MapBatchShape = MapCellShape/BatchCellShape;
            ChunkCellShape = 1 << mapDataInfo.MaxLod;
            ChunkBatchShape = new int2(1 << (mapDataInfo.MaxLod - currentLod), 1 << (mapDataInfo.MaxLod - currentLod));
        }
        
        public int GetBatchCellIndexByBatchCellCoord(int2 batchCellCoord)
        {
            return batchCellCoord.y * BatchCellShape.x + batchCellCoord.x;
        }
        
        public int2 GetMapChunkCoordByMapBatchCoord(int2 mapBatchCoord)
        {
            return mapBatchCoord / ChunkBatchShape;
        }
        
        public int2 GetChunkBatchCoordByMapBatchCoord(int2 mapBatchCoord)
        {
            return mapBatchCoord % ChunkBatchShape;
        }
        
        public int2 GetBatchCellCoordByIndex(int batchCellIndex)
        {
            return new int2(batchCellIndex % BatchCellShape.x, batchCellIndex / BatchCellShape.x);
        }
        
        
        public int2 GetMapBatchCoordByMapBatchIndex(int mapCoord)
        {
            return new int2(mapCoord % MapBatchShape.x, mapCoord / MapBatchShape.x);
        }
        
        public int2 GetMapCellCoordByMapBatchCoordAndOffset(int2 mapBatchCoord, int2 offset)
        {
           return mapBatchCoord * BatchCellShape + offset;
        }
        
        public int MapCellCoordToIndex(int2 mapCellCoord)
        {
            return mapCellCoord.y * MapCellShape.x + mapCellCoord.x;
        }
        
        public int MapCellCoordToObstacleIndex(int2 mapCellCoord)
        {
            return mapCellCoord.y * MapDataInfo.ObstacleShape.x + mapCellCoord.x;
        }
        
        public int GetMapCellIndexByMapBatchCoordAndOffset(int2 mapBatchCoord, int2 offset)
        {
            var mapCellCoord = GetMapCellCoordByMapBatchCoordAndOffset(mapBatchCoord, offset);
            return MapCellCoordToIndex(mapCellCoord);
        }

    }
}