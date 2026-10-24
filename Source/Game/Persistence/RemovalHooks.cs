using System;
using System.Collections.Generic;
using HarmonyLib;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Persistence
{
    // What happens to RSC data when an RSC block leaves the world, server side.
    // BlockRscNode marks a position as destroyed (damage, explosion, collapse)
    // just before the game removes the block; every other removal (pickup, admin
    // tools) is non-destructive.
    //
    // Drive cabinet (DISK-04/06): destroyed -> one survival roll per disk;
    //   survivors drop intact, the rest lose their records for good. Otherwise
    //   every disk drops intact. The cabinet's slot map is cleared first, so a
    //   repeated event finds nothing to drop or delete twice.
    // Settings (priority/filter, DISK-05): destroyed -> kept for a rebuilt block
    //   at the same spot, but the filter is lost with FilterLossChance. Otherwise
    //   they are cleared, so a newly placed block starts from the defaults.
    internal static class RemovalHooks
    {
        private static readonly HashSet<Vector3i> Destroyed = new HashSet<Vector3i>();
        private static readonly System.Random Rng = new System.Random();

        public static void MarkDestroyed(Vector3i pos)
        {
            if (ConnectionManager.Instance.IsServer)
                Destroyed.Add(pos);
        }

        public static void OnRemoved(Vector3i pos, NodeKind kind)
        {
            bool destroyed = Destroyed.Remove(pos);
            if (kind == NodeKind.DriveCabinet)
                ReleaseDisks(pos, destroyed);
            UpdateSettings(pos, destroyed);
        }

        public static void Reset() => Destroyed.Clear();

        private static double Roll() => Rng.NextDouble();

        private static void ReleaseDisks(Vector3i pos, bool destroyed)
        {
            var store = DiskFile.Store;
            if (store == null)
                return;
            var installed = store.RemoveCabinet(PositionKey.Pack(pos.x, pos.y, pos.z));
            if (installed.Count == 0)
                return;
            var survivors = new List<Guid>();
            var lost = new List<Guid>();
            if (destroyed)
                DestructionRules.RollDisks(installed, Config.RscSettings.Current.DiskSurvivalChance, Roll, survivors, lost);
            else
                survivors.AddRange(installed);
            var dropAt = pos.ToVector3() + UnityEngine.Vector3.one * 0.5f;
            foreach (var id in survivors)
            {
                var record = store.Get(id);
                if (record != null)
                    Drops.Drop(DiskItems.Create(record.Tier, id, store.WorldId), 1, dropAt);
            }
            foreach (var id in lost)
                store.Destroy(id);
            RscLog.Event("block", "cabinet at " + pos + (destroyed ? " destroyed: " : " removed: ") + survivors.Count
                + " disk(s) dropped, " + lost.Count + " lost");
        }

        private static void UpdateSettings(Vector3i pos, bool destroyed)
        {
            var store = NodeConfigFile.Store;
            long key = PositionKey.Pack(pos.x, pos.y, pos.z);
            var config = store.Get(key);
            if (config.IsDefault)
                return;
            if (!destroyed)
            {
                store.Remove(key);
                RscLog.Event("block", "settings cleared for removed block at " + pos);
                return;
            }
            if (config.Filter.Mode != FilterMode.None && DestructionRules.LosesFilter(Config.RscSettings.Current.FilterLossChance, Roll))
            {
                store.Set(key, new NodeConfig(config.Priority, ItemFilter.None));
                RscLog.Event("block", "block at " + pos + " destroyed: filter lost, priority kept for a rebuilt block");
            }
        }
    }

    [HarmonyPatch(typeof(BlockPowered), nameof(BlockPowered.OnBlockRemoved))]
    internal static class BlockRemovedPatch
    {
        private static void Postfix(Vector3i _blockPos, BlockValue _blockValue)
        {
            if (_blockValue.ischild || !ConnectionManager.Instance.IsServer)
                return;
            if (Topology.RscBlocks.TryGetRole(_blockValue.Block, out var kind, out _))
                RemovalHooks.OnRemoved(_blockPos, kind);
        }
    }
}
