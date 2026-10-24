using System;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Providers
{
    public enum DiskState { NotADisk, Blank, Ready, OtherWorld, Missing }

    // Disk items (rscDisk1k ... rscDisk1m). The item carries only its identity in
    // native item metadata, which travels through inventories, drops and saves
    // (P0 finding 4): the disk id and the id of the world it belongs to. The
    // contents live in the world's DiskStore.
    internal static class DiskItems
    {
        public const string DiskKey = "rsc.disk";
        public const string WorldKey = "rsc.world";
        private const string TierProperty = "RscDiskTier";

        public static bool TryGetTier(ItemValue item, out DiskTier tier)
        {
            tier = default;
            var props = item?.ItemClass?.Properties;
            return props != null && props.Values.TryGetValue(TierProperty, out string value)
                && Enum.TryParse(value, out tier) && Enum.IsDefined(typeof(DiskTier), tier);
        }

        public static Guid? DiskId(ItemValue item) => ReadGuid(item, DiskKey);

        public static DiskState Describe(ItemValue item, DiskStore store, out DiskRecord record)
        {
            record = null;
            if (!TryGetTier(item, out _))
                return DiskState.NotADisk;
            var id = DiskId(item);
            if (!id.HasValue)
                return DiskState.Blank;
            if (store == null || ReadGuid(item, WorldKey) != store.WorldId)
                return DiskState.OtherWorld; // DISK-08
            record = store.Get(id.Value);
            return record == null ? DiskState.Missing : DiskState.Ready;
        }

        public static void Tag(ItemValue item, Guid disk, Guid world)
        {
            item.SetMetadata(DiskKey, disk.ToString("N"));
            item.SetMetadata(WorldKey, world.ToString("N"));
        }

        // A disk item for an installed disk being taken out of a cabinet.
        public static ItemValue Create(DiskTier tier, Guid disk, Guid world)
        {
            var item = ItemClass.GetItem(ItemName(tier));
            if (item == null || item.IsEmpty())
                throw new InvalidOperationException("Disk item missing from items.xml: " + ItemName(tier));
            Tag(item, disk, world);
            return item;
        }

        public static string ItemName(DiskTier tier)
        {
            switch (tier)
            {
                case DiskTier.K1: return "rscDisk1k";
                case DiskTier.K10: return "rscDisk10k";
                case DiskTier.K100: return "rscDisk100k";
                case DiskTier.K250: return "rscDisk250k";
                case DiskTier.K500: return "rscDisk500k";
                default: return "rscDisk1m";
            }
        }

        private static Guid? ReadGuid(ItemValue item, string key) =>
            item != null && item.TryGetMetadata(key, out string text) && Guid.TryParseExact(text, "N", out var id)
                ? id
                : (Guid?)null;
    }
}
