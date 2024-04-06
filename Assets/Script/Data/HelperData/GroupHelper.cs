using Script.PathFind;
using Unity.Mathematics;
using UnityEngine;

public struct GroupHelper
{
    public const int MaxLod = 8;
    public const int LodBitCount = 4;
    public const int ChunkIdBitHalfCount = 16 - MaxLod - LodBitCount / 2;

    public static int GetLod(GroupId groupId)
    {
        return (int)((groupId.Value >> (32 - LodBitCount)) & ((1 << LodBitCount) - 1));
    }


    public static int2 GetChunkId(GroupId groupId)
    {
        var chunkBit = (int)(groupId.Value >> MaxLod * 2);
        var mask = (1 << ChunkIdBitHalfCount) - 1;
        int y = chunkBit & (mask);
        int x = (chunkBit >> ChunkIdBitHalfCount) & (mask);
        return new int2(x, y);
    }

    public static int2 GetBatchId(GroupId groupId)
    {
        var lod = GetLod(groupId);
        var lodOffsetBit = 2 * lod;
        var mask = (1 << (MaxLod - lod)) - 1;
        var y = (int)(groupId >> lodOffsetBit) & (mask);
        var x = (int)(groupId >> (lodOffsetBit + MaxLod - lod)) & (mask);
        return new int2(x, y);
    }

    public static int GetOffset(GroupId groupId)
    {
        var lod = GetLod(groupId);
        var offsetMask = ((1 << (lod * 2)) - 1);
        return (int)(groupId & offsetMask);
    }

    public static GroupId RemoveOffset(GroupId groupId)
    {
        var offsetCount = GetLod(groupId) * 2;
        var newGroupId = groupId >> offsetCount << offsetCount;
        return new GroupId()
        {
            Value = newGroupId
        };
    }


    
    public static GroupId GenGroupId(int2 chunkId, int2 batchId, int offset, int lod)
    {
#if UNITY_EDITOR
        Debug.Assert(chunkId is { x: >= 0, y: >= 0 } && chunkId.x <= (1 << ChunkIdBitHalfCount) - 1 && chunkId.y <= (1 << ChunkIdBitHalfCount) - 1);
        Debug.Assert(batchId.x >= 0 && batchId.y >= 0 && batchId.x <= (1 << (MaxLod - lod)) - 1 && batchId.y <= (1 << (MaxLod - lod)) - 1);
        Debug.Assert(offset >= 0 && offset <= (1 << 2 * lod - 1));
        Debug.Assert(lod > 0 && lod <= MaxLod);
#endif
        var chunkBit = (chunkId.x << ChunkIdBitHalfCount) | chunkId.y;
        var y = batchId.y << (2 * lod);
        var x = batchId.x << (MaxLod + lod);
        return new GroupId()
        {
            Value = (uint)((chunkBit << (MaxLod * 2)) | x | y | offset) | ((uint)lod << (32 - LodBitCount))
        };
    }
    
    public static Position Int2ToPosition(int2 pos)
    {
        return new Position()
        {
            x = (ushort)pos.x,
            y = (ushort)pos.y
        };
    }
    
}