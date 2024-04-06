using Unity.Mathematics;
using UnityEngine.Serialization;

namespace Script.PathFind
{
    public struct MapDataInfo
    {
        public int2 AllGroupShape;
        public int2 ObstacleShape;
        public int StartLod;
        public int MaxLod;

        public MapDataInfo(int2 obstacleShape, int startLod, int maxLod)
        {
            ObstacleShape = obstacleShape;
            var remain = obstacleShape % (1 << maxLod) == 0;
            AllGroupShape = (obstacleShape / (1 << maxLod) + new int2(remain.x ? 0 : 1, remain.y ? 0 : 1))<<maxLod;
            StartLod = startLod;
            MaxLod = maxLod;
        }
    }
}