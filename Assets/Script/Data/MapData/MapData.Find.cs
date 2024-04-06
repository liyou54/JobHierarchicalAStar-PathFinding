using Unity.Collections;

namespace Script.PathFind
{
    public partial struct MapData
    {
        public void FindPath(Position start, Position end, ObstacleType ownObstacleType)
        {
            var startGroupId = GetGroupIdByPosition(start);
            var endGroupId = GetGroupIdByPosition(end);
            if (startGroupId == GroupId.InValid || endGroupId == GroupId.InValid)
            {
                return;
            }
            
            TryGetSameParent(startGroupId,endGroupId,
                Allocator.TempJob, out var srcParent,out var dstParent);
            
        }
        
        private bool TryGetSameParent(GroupId src,GroupId dst,
            Allocator allocator,
            out NativeList<GroupId> srcParent,
            out NativeList<GroupId> dstParent
        )
        {
            srcParent = new NativeList<GroupId>(allocator);
            dstParent = new NativeList<GroupId>(allocator);
            srcParent.Add(src);
            dstParent.Add(dst);
            while (true)
            {
                var srcInfo = GetGroupInfoByGroupId(src);
                var dstInfo = GetGroupInfoByGroupId(dst);
                if (srcInfo.ParentGroupId == GroupId.InValid || dstInfo.ParentGroupId == GroupId.InValid)
                {
                    return false;
                }

                srcParent.Add(srcInfo.ParentGroupId);
                dstParent.Add(dstInfo.ParentGroupId);
                if (srcInfo.ParentGroupId == dstInfo.ParentGroupId)
                {
                    return true;
                }

                src = srcInfo.ParentGroupId;
                dst = dstInfo.ParentGroupId;
            }
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