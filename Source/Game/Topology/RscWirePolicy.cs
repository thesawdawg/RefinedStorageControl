using RefinedStorageControl.Core;

namespace RefinedStorageControl.Game.Topology
{
    // Server-side decision for a pending SetParent(child, newParent) wire.
    // Wires that would join two storage controllers are rejected — this
    // implements the design's recommended "reject the joining wire" (F02 is
    // still open, so rejection is a policy choice, not a settled rule).
    // Non-RSC<->RSC wiring is allowed: those wires only carry power (open
    // policy question whether vanilla consumers may draw through RSC nodes).
    internal static class RscWirePolicy
    {
        public static (bool allowed, string reason) Evaluate(Vector3i childPos, Vector3i newParentPos)
        {
            var world = GameManager.Instance.World;
            if (world == null)
                return (true, "no world");
            bool childRsc = RscBlocks.TryGetRole(world.GetBlock(childPos).Block, out _, out _);
            bool parentRsc = RscBlocks.TryGetRole(world.GetBlock(newParentPos).Block, out _, out _);
            if (!childRsc && !parentRsc)
                return (true, "not an RSC wire");
            if (!childRsc || !parentRsc)
                return (true, "non-RSC endpoint");

            var registry = ServerTopology.Snapshot();
            var childId = RscBlocks.ToNodeId(childPos);
            var parentId = RscBlocks.ToNodeId(newParentPos);

            // Re-parenting moves the child's whole subtree: detach the current
            // RSC parent edge first (a node has at most one parent).
            var childItem = ServerTopology.PowerItemAt(childPos);
            var oldParent = childItem?.Parent;
            if (oldParent != null)
            {
                var oldParentId = RscBlocks.ToNodeId(oldParent.Position);
                if (registry.EvaluateLink(childId, oldParentId) == LinkCheck.AlreadyLinked)
                    registry.Unlink(childId, oldParentId);
            }

            if (registry.EvaluateLink(childId, parentId) == LinkCheck.ControllerConflict)
                return (false, "would join two storage controllers");
            return (true, "ok");
        }
    }
}
