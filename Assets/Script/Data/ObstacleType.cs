using System;

namespace Script.PathFind
{
    [Flags]
    public enum ObstacleType
    {
        Default = 0,
        Tree = 1,
        Water = 1<<1,
        Hard = 0xfffffff,
    }
}