using System;

namespace RefinedStorageControl.Core
{
    // Lossless packing of a world block position into a NodeId's long value:
    // x,z within +/-2^20 (21 bits each), y within 0..255 (8 bits).
    public static class PositionKey
    {
        public const int MaxHorizontal = 1 << 20; // 1048576

        public static long Pack(int x, int y, int z)
        {
            if (x < -MaxHorizontal || x >= MaxHorizontal)
                throw new ArgumentOutOfRangeException(nameof(x), x, "Out of position key range.");
            if (z < -MaxHorizontal || z >= MaxHorizontal)
                throw new ArgumentOutOfRangeException(nameof(z), z, "Out of position key range.");
            if (y < 0 || y > 255)
                throw new ArgumentOutOfRangeException(nameof(y), y, "Out of position key range.");
            return ((x + (long)MaxHorizontal) << 29) | ((z + (long)MaxHorizontal) << 8) | (long)y;
        }

        public static void Unpack(long key, out int x, out int y, out int z)
        {
            y = (int)(key & 0xFFL);
            z = (int)((key >> 8) & 0x1FFFFFL) - MaxHorizontal;
            x = (int)((key >> 29) & 0x1FFFFFL) - MaxHorizontal;
        }
    }
}
