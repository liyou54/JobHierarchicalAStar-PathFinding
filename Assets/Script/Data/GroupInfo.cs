using UnityEngine.Serialization;

namespace Script.PathFind
{
    public struct GroupInfo
    {
        public GroupId GroupId;
        public GroupId ParentGroupId;
        public Position BatchCellCoordPosition;
        public ObstacleType ObstacleType;
        public int Size;
        public GroupId RepresentGroupId;
    }
}