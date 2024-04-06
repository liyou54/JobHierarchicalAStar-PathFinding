using System;

namespace Script.PathFind
{
    public struct GroupId : IEquatable<GroupId>
    {
        public uint Value;
        public static readonly GroupId InValid = new GroupId() { Value = 0 };

        public bool IsValid()
        {
            return this != InValid;
        }

        public static implicit operator uint(GroupId val) => val.Value;
        public static implicit operator GroupId(uint val) => new GroupId { Value = val };

        public  int GetBatchCellSize()
        {
            var lod = GroupHelper.GetLod(this);
            return 1 << lod;
        }

        
        public override string ToString()
        {
            var lod = GroupHelper.GetLod(this);
            var chunkId = GroupHelper.GetChunkId(this);
            var batchId = GroupHelper.GetBatchId(this);
            var offset = GroupHelper.GetOffset(this);
            return $"Lod({lod}) Chunk({chunkId.x},{chunkId.y}) Batch({batchId.x},{batchId.y}),Offset({offset})";
        }

        public bool Equals(GroupId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is GroupId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)Value;
        }

    }
}