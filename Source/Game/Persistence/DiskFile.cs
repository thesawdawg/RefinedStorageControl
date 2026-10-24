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
                RscLog.Out("disk store loaded: " + store.DiskCount + " disks, world " + store.WorldId);
                return;
            }
            Store = new DiskStore(Guid.NewGuid()) { Dirty = true };
            RscLog.Out("disk store created for this world: " + Store.WorldId);
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
