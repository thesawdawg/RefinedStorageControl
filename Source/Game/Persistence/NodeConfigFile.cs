using System;
using System.IO;
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
        private const string FolderName = "RefinedStorageControl";
        private const string FileName = "nodes.dat";

        private static string _loadedFrom;

        public static NodeConfigStore Store { get; private set; } = new NodeConfigStore();

        public static void Load()
        {
            Store = new NodeConfigStore();
            _loadedFrom = null;
            if (!ConnectionManager.Instance.IsServer)
                return;
            string path = PathFor(GameIO.GetSaveGameDir());
            _loadedFrom = path;
            if (TryRead(path, out var store) || TryRead(path + ".bak", out store))
            {
                Store = store;
                RscLog.Out("node config loaded: " + store.Count + " configured nodes");
                return;
            }
            if (File.Exists(path) || File.Exists(path + ".bak"))
            {
                // Keep the unreadable files for diagnosis; never overwrite them silently.
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                foreach (var p in new[] { path, path + ".bak" })
                    if (File.Exists(p))
                        File.Move(p, p + ".unreadable-" + stamp);
                RscLog.Error("node config unreadable; starting with defaults. Old files kept with suffix .unreadable-" + stamp);
            }
        }

        public static void SaveIfNeeded()
        {
            if (_loadedFrom == null || !ConnectionManager.Instance.IsServer || !Store.Dirty)
                return;
            if (!string.Equals(_loadedFrom, PathFor(GameIO.GetSaveGameDir()), StringComparison.OrdinalIgnoreCase))
                return; // a different game is active; never write this store into it
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_loadedFrom));
                string tmp = _loadedFrom + ".tmp";
                using (var writer = new BinaryWriter(File.Create(tmp)))
                    NodeConfigSerializer.Write(writer, Store);
                if (File.Exists(_loadedFrom))
                    File.Copy(_loadedFrom, _loadedFrom + ".bak", overwrite: true);
                if (File.Exists(_loadedFrom))
                    File.Delete(_loadedFrom);
                File.Move(tmp, _loadedFrom);
                Store.Dirty = false;
            }
            catch (Exception e)
            {
                RscLog.Error("node config save failed: " + e);
            }
        }

        private static string PathFor(string saveDir) => Path.Combine(Path.Combine(saveDir, FolderName), FileName);

        private static bool TryRead(string path, out NodeConfigStore store)
        {
            store = null;
            if (!File.Exists(path))
                return false;
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(path)))
                    store = NodeConfigSerializer.Read(reader);
                return true;
            }
            catch (Exception e)
            {
                RscLog.Warning("node config read failed for " + path + ": " + e.Message);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(World), nameof(World.Save))]
    internal static class WorldSavePatch
    {
        private static void Postfix() => NodeConfigFile.SaveIfNeeded();
    }

    // A removed RSC block loses its settings, so a new block placed at the same
    // position starts from the defaults. Fires only on real removal, not unload.
    [HarmonyPatch(typeof(BlockPowered), nameof(BlockPowered.OnBlockRemoved))]
    internal static class BlockRemovedPatch
    {
        private static void Postfix(Vector3i _blockPos, BlockValue _blockValue)
        {
            if (_blockValue.ischild || !ConnectionManager.Instance.IsServer)
                return;
            if (!Topology.RscBlocks.TryGetRole(_blockValue.Block, out _, out _))
                return;
            if (NodeConfigFile.Store.Remove(Core.PositionKey.Pack(_blockPos.x, _blockPos.y, _blockPos.z)))
                RscLog.Out("node config cleared for removed block at " + _blockPos);
        }
    }
}
