using System;

namespace Script.PathFind
{
    public struct EdgeInfo : IEquatable<EdgeInfo>
    {
        public GroupId SrcGroupId;
        public GroupId DstGroupId;
        public ObstacleType ObstacleType;

        public EdgeInfo(GroupId srcGroupId, GroupId dstGroupId, ObstacleType obstacleType)
        {
            SrcGroupId = srcGroupId;
            DstGroupId = dstGroupId;
            ObstacleType = obstacleType;
        }

        public override string ToString()
        {
            return SrcGroupId + " -> " + DstGroupId;
        }


        public bool Equals(EdgeInfo other)
        {
            return SrcGroupId.Equals(other.SrcGroupId) && DstGroupId.Equals(other.DstGroupId) && ObstacleType == other.ObstacleType;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                uint hash = 17;
                hash = hash * 23 + SrcGroupId;
                hash = hash * 29 + DstGroupId;
                hash = hash * 31 + (uint)ObstacleType;
                return (int)hash;
            }
        }
    }
}