using System;

namespace RefinedStorageControl.Storage
{
    public enum DiskTier { K1 = 1, K10, K100, K250, K500, M1 }

    public static class DiskCapacity
    {
        // Individual items per drive, matching the disk item requirements.
        public static long Of(DiskTier tier)
        {
            switch (tier)
            {
                case DiskTier.K1: return 1_000;
                case DiskTier.K10: return 10_000;
                case DiskTier.K100: return 100_000;
                case DiskTier.K250: return 250_000;
                case DiskTier.K500: return 500_000;
                case DiskTier.M1: return 1_000_000;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "Undefined disk tier.");
            }
        }
    }
}
