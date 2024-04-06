using Unity.Mathematics;

namespace Script.PathFind
{
    public static class Int2Util
    {
        public static int ToSize(this int2 shape)
        {
            return shape.x * shape.y;
        }
    }
}