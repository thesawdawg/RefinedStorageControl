using System.Collections.Generic;
using RefinedStorageControl.Core;

namespace RefinedStorageControl.Game.Topology
{
    // Server-side projection of the native power graph restricted to RSC nodes.
    // Links exist only between RSC nodes: data does not pass through vanilla
    // power devices even when they sit inside the wire tree.
    internal static class ServerTopology
    {
        public static NetworkRegistry Snapshot()
        {
            var registry = new NetworkRegistry();
            var pm = PowerManager.Instance;
            var world = GameManager.Instance.World;
            if (pm == null || world == null)
                return registry;

            var nodes = new HashSet<Vector3i>();
            foreach (var item in pm.PowerItemDictionary.Values)
            {
                var block = world.GetBlock(item.Position).Block;
                if (!RscBlocks.TryGetRole(block, out var kind, out var tier))
                    continue;
                registry.AddNode(RscBlocks.ToNodeId(item.Position), kind, tier);
                nodes.Add(item.Position);
            }
            foreach (var item in pm.PowerItemDictionary.Values)
            {
                if (item.Parent == null || !nodes.Contains(item.Position) || !nodes.Contains(item.Parent.Position))
                    continue;
                // LoadLink bypasses validation: if a multi-controller topology
                // ever exists on disk it must surface as Conflict, not be hidden.
                registry.LoadLink(RscBlocks.ToNodeId(item.Position), RscBlocks.ToNodeId(item.Parent.Position));
            }
            return registry;
        }

        public static PowerItem PowerItemAt(Vector3i pos) =>
            PowerManager.Instance?.GetPowerItemByWorldPos(pos);
    }
}
