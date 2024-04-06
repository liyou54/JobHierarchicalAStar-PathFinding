using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Serialization;

namespace Script.PathFind
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Position
    {
        [FormerlySerializedAs("X")] public ushort x;
        [FormerlySerializedAs("Y")] public ushort y;
        
        public Position(ushort x, ushort y)
        {
            this.x = x;
            this.y = y;
        }

        public override string ToString()
        {
            return $"({x},{y})";
        }
        
        public float Distance(Position other)
        {
            return math.distance(ToInt2(), other.ToInt2());
        }
        
        public readonly int2 ToInt2()
        {
            return new int2(x, y);
        }
    }
}