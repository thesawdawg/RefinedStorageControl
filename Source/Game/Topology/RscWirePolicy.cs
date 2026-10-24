using RefinedStorageControl.Core;

namespace RefinedStorageControl.Game.Topology
{
    // Server-side decision for a pending SetParent(child, newParent) wire.
    // NET-06: only a controller may be wired from vanilla power; RSC blocks only
    // feed RSC blocks. NET-04: a wire that would put two controllers on one
    // network is rejected; moving a branch between networks is otherwise fine.
    internal static class RscWirePolicy
    {
        public static (bool allowed, string reason, string tooltipKey) Evaluate(Vector3i childPos, Vector3i newParentPos)
        {
            var world = GameManager.Instance.World;
            if (world == null)
                return (true, "no world", null);
            NodeKind? childKind = RscBlocks.TryGetRole(world.GetBlock(childPos).Block, out var ck, out _) ? ck : (NodeKind?)null;
            NodeKind? parentKind = RscBlocks.TryGetRole(world.GetBlock(newParentPos).Block, out var pk, out _) ? pk : (NodeKind?)null;
            if (!childKind.HasValue && !parentKind.HasValue)
                return (true, "not an RSC wire", null);

            switch (NetworkRegistry.EvaluateWireKinds(childKind, parentKind))
            {
                case LinkCheck.VanillaChild:
                    return (false, "storage blocks cannot power vanilla devices", "rscWireVanillaChild");
                case LinkCheck.VanillaParent:
                    return (false, "only a storage controller can connect to vanilla wiring", "rscWireVanillaParent");
            }
            if (!childKind.HasValue || !parentKind.HasValue)
                return (true, "controller on vanilla power", null);

            var registry = ServerTopology.Snapshot();
            var oldParent = ServerTopology.PowerItemAt(childPos)?.Parent;
            NodeId? oldParentId = oldParent != null ? RscBlocks.ToNodeId(oldParent.Position) : (NodeId?)null;
            var check = registry.EvaluateReparent(RscBlocks.ToNodeId(childPos), oldParentId, RscBlocks.ToNodeId(newParentPos));
            if (check == LinkCheck.ControllerConflict)
                return (false, "would join two storage controllers", "rscWireConflict");
            return (true, "ok", null);
        }
    }
}
