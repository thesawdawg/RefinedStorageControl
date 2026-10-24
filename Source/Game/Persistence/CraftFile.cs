using System;
using RefinedStorageControl.Crafting;

namespace RefinedStorageControl.Game.Persistence
{
    // Owns the host's CraftJobStore: <save>/RefinedStorageControl/jobs.dat
    // (+ .bak). Dispatched jobs and banked XP survive a restart (CRAFT-09); a
    // request still counting down has moved nothing and is not saved. Written
    // after each World.Save like the other RSC files.
    internal static class CraftFile
    {
        private const string FileName = "jobs.dat";

        private static string _loadedFrom;

        // Null on clients and before a game has started.
        public static CraftJobStore Store { get; private set; }

        public static void Load()
        {
            Store = null;
            _loadedFrom = null;
            if (!ConnectionManager.Instance.IsServer)
                return;
            _loadedFrom = RscSaveFile.PathFor(FileName);
            if (RscSaveFile.TryLoad(_loadedFrom, CraftJobSerializer.Read, "craft jobs", out var store))
            {
                Store = store;
                RscLog.Out("craft jobs loaded: " + store.Count + " jobs");
                return;
            }
            Store = new CraftJobStore();
        }

        public static void SaveIfNeeded()
        {
            var store = Store;
            if (store == null || _loadedFrom == null || !ConnectionManager.Instance.IsServer || !store.Dirty)
                return;
            if (!string.Equals(_loadedFrom, RscSaveFile.PathFor(FileName), StringComparison.OrdinalIgnoreCase))
                return; // a different game is active; never write this store into it
            if (RscSaveFile.Write(_loadedFrom, w => CraftJobSerializer.Write(w, store), "craft jobs"))
                store.Dirty = false;
        }
    }
}
