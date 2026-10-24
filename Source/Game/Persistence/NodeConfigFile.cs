using System;
using HarmonyLib;
using RefinedStorageControl.Routing;

namespace RefinedStorageControl.Game.Persistence
{
    // Owns the host's NodeConfigStore and its file in the save directory:
    // <save>/RefinedStorageControl/nodes.dat, with nodes.dat.bak holding the
    // previous good copy (the power.dat/.bak pattern). Loaded at game start and
    // written right after each World.Save (P0 finding 5: no shared transaction
    // with chunk saves). WorldShuttingDown fires before the final save, so the
    // store stays in memory until the next game start replaces it.
    internal static class NodeConfigFile
    {
        private const string FileName = "nodes.dat";

        private static string _loadedFrom;

        public static NodeConfigStore Store { get; private set; } = new NodeConfigStore();

        public static void Load()
        {
            Store = new NodeConfigStore();
            _loadedFrom = null;
            if (!ConnectionManager.Instance.IsServer)
                return;
            _loadedFrom = RscSaveFile.PathFor(FileName);
            if (RscSaveFile.TryLoad(_loadedFrom, NodeConfigSerializer.Read, "node config", out var store))
            {
                Store = store;
                RscLog.Out("node config loaded: " + store.Count + " configured nodes");
            }
        }

        public static void SaveIfNeeded()
        {
            if (_loadedFrom == null || !ConnectionManager.Instance.IsServer || !Store.Dirty)
                return;
            if (!string.Equals(_loadedFrom, RscSaveFile.PathFor(FileName), StringComparison.OrdinalIgnoreCase))
                return; // a different game is active; never write this store into it
            if (RscSaveFile.Write(_loadedFrom, w => NodeConfigSerializer.Write(w, Store), "node config"))
                Store.Dirty = false;
        }
    }

    [HarmonyPatch(typeof(World), nameof(World.Save))]
    internal static class WorldSavePatch
    {
        private static void Postfix()
        {
            NodeConfigFile.SaveIfNeeded();
            DiskFile.SaveIfNeeded();
            CraftFile.SaveIfNeeded();
        }
    }
}
