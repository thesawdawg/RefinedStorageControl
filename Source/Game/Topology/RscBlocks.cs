using RefinedStorageControl.Core;

namespace RefinedStorageControl.Game.Topology
{
    // Mapping between RSC block properties and the pure topology enums.
    internal static class RscBlocks
    {
        public static bool TryGetRole(Block block, out NodeKind kind, out ControllerTier? tier)
        {
            kind = NodeKind.Hub;
            tier = null;
            if (block == null || !block.Properties.Values.TryGetValue("RscRole", out string role))
                return false;
            switch (role)
            {
                case "Controller":
                    kind = NodeKind.Controller;
                    if (block.Properties.Values.TryGetValue("RscTier", out string tierName))
                    {
                        if (tierName == "Simple") tier = ControllerTier.Simple;
                        else if (tierName == "Advanced") tier = ControllerTier.Advanced;
                    }
                    return tier.HasValue;
                case "Hub": kind = NodeKind.Hub; return true;
                case "Interface": kind = NodeKind.Interface; return true;
                case "Terminal": kind = NodeKind.Terminal; return true;
                case "DriveCabinet": kind = NodeKind.DriveCabinet; return true;
                default: return false;
            }
        }

        public static NodeId ToNodeId(Vector3i pos) => new NodeId(PositionKey.Pack(pos.x, pos.y, pos.z));

        public static Vector3i FromNodeId(NodeId id)
        {
            PositionKey.Unpack(id.Value, out int x, out int y, out int z);
            return new Vector3i(x, y, z);
        }
    }
}
