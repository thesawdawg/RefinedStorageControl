using System;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Persistence
{
    // Owns the host's DiskStore: <save>/RefinedStorageControl/disks.dat (+ .bak).
    // A world without the file gets a fresh store with a new world id, which is
    // what binds disks to this world (DISK-08). Written after each World.Save,
    // like the interface settings.
    internal static class DiskFile
    {
        private const string FileName = "disks.dat";

        private static string _loadedFrom;

        // Null on clients and before a game has started.
        public static DiskStore Store { get; private set; }

        public static void Load()
        {
            Store = null;
            _loadedFrom = null;
            if (!ConnectionManager.Instance.IsServer)
                return;
            _loadedFrom = RscSaveFile.PathFor(FileName);
            if (RscSaveFile.TryLoad(_loadedFrom, DiskStoreSerializer.Read, "disk store", out var store))
            {
                Store = store;
                int merged = NormalizeKeys(store);
                RscLog.Out("disk store loaded: " + store.DiskCount + " disks, world " + store.WorldId
                    + (merged > 0 ? "; merged " + merged + " entries that differed only by seed" : ""));
                return;
            }
            Store = new DiskStore(Guid.NewGuid()) { Dirty = true };
            RscLog.Out("disk store created for this world: " + Store.WorldId);
        }

        // Disks written before stackable items were keyed without their seed
        // hold one entry per batch; re-key them so they merge. Totals are kept:
        // each entry is moved whole, so capacity use does not change.
        private static int NormalizeKeys(DiskStore store)
        {
            int moved = 0;
            foreach (var disk in store.Disks)
                foreach (var pair in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ItemKey, long>>(disk.Items))
                {
                    var item = ItemKeys.ToItemValue(pair.Key);
                    if (item.ItemClass == null)
                        continue; // unknown type: keep the record untouched
                    var key = ItemKeys.FromItemValue(item);
                    if (key.Equals(pair.Key))
                        continue;
                    disk.Extract(pair.Key, pair.Value);
                    disk.Insert(key, pair.Value);
                    moved++;
                }
            if (moved > 0)
                store.Dirty = true;
            return moved;
        }

        public static void SaveIfNeeded()
        {
            var store = Store;
            if (store == null || _loadedFrom == null || !ConnectionManager.Instance.IsServer || !store.Dirty)
                return;
            if (!string.Equals(_loadedFrom, RscSaveFile.PathFor(FileName), StringComparison.OrdinalIgnoreCase))
                return; // a different game is active; never write this store into it
            if (RscSaveFile.Write(_loadedFrom, w => DiskStoreSerializer.Write(w, store), "disk store"))
                store.Dirty = false;
        }
    }
}
