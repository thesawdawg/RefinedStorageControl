using System;
using System.Linq;
using RefinedStorageControl.Core;
using static Harness;

internal static class CoreTests
{
    public static void Run()
    {
        var net = new NetworkRegistry();
        var controller = new NodeId(1);
        var hub = new NodeId(2);
        var iface = new NodeId(3);
        var terminal = new NodeId(4);
        net.AddNode(controller, NodeKind.Controller, ControllerTier.Simple);
        net.AddNode(hub, NodeKind.Hub);
        net.AddNode(iface, NodeKind.Interface);
        net.AddNode(terminal, NodeKind.Terminal);
        Check(net.NodeCount == 4, "Registry must track node count.");
        Check(net.Contains(hub) && !net.Contains(new NodeId(99)),
            "Contains must report registered nodes only (unwired blocks are never registered).");

        Reject(() => net.AddNode(controller, NodeKind.Terminal), "Duplicate node ids must be rejected.");
        Reject<ArgumentException>(() => net.AddNode(new NodeId(9), NodeKind.Controller),
            "Controllers must declare a tier.");
        Reject<ArgumentException>(() => net.AddNode(new NodeId(9), NodeKind.Hub, ControllerTier.Simple),
            "Non-controllers must not declare a tier.");

        net.Link(controller, hub);
        net.Link(hub, iface);
        net.Link(iface, terminal);
        foreach (var id in new[] { controller, hub, iface, terminal })
        {
            var status = net.GetStatus(id);
            Check(status.State == NodeState.Connected && status.Controller == controller,
                "Every node in a single-controller component must be Connected: " + id);
            Check(net.IsConnected(id, controller), "IsConnected must hold inside the component: " + id);
        }
        Check(net.Members(controller).Select(n => n.Value).SequenceEqual(new long[] { 1, 2, 3, 4 }),
            "Members must list the whole component sorted by id.");
        Check(net.TierOf(controller) == ControllerTier.Simple && net.TierOf(hub) == null,
            "Tiers must be reported for controllers only.");

        Check(net.EvaluateLink(iface, terminal) == LinkCheck.AlreadyLinked, "Existing links must report AlreadyLinked.");
        Check(net.EvaluateLink(iface, iface) == LinkCheck.SelfLink, "Self links must report SelfLink.");
        Check(net.EvaluateLink(iface, new NodeId(77)) == LinkCheck.UnknownNode, "Unknown nodes must report UnknownNode.");

        var other = new NodeId(5);
        net.AddNode(other, NodeKind.Controller, ControllerTier.Advanced);
        var otherStatus = net.GetStatus(other);
        Check(otherStatus.State == NodeState.Connected && otherStatus.Controller == other,
            "A standalone controller must be Connected to itself.");
        Check(net.EvaluateLink(other, hub) == LinkCheck.ControllerConflict,
            "Linking two controller components must report ControllerConflict.");
        Reject(() => net.Link(other, hub), "Conflicting links must throw.");
        Check(net.EvaluateLink(other, hub) == LinkCheck.ControllerConflict && net.GetStatus(other).Controller == other,
            "A rejected link must leave the topology unchanged.");

        // Re-parenting: hub -> iface -> terminal is a branch under controller.
        Check(net.EvaluateReparent(iface, hub, other) == LinkCheck.Ok,
            "Moving a controller-free branch to another network must be allowed.");
        Check(net.EvaluateLink(other, hub) == LinkCheck.ControllerConflict && net.IsConnected(terminal, controller),
            "Evaluating a re-parent must leave the topology unchanged.");
        Check(net.EvaluateReparent(iface, null, other) == LinkCheck.ControllerConflict,
            "A second parent without detaching must still report ControllerConflict.");
        Check(net.EvaluateReparent(hub, controller, other) == LinkCheck.Ok,
            "Detaching a branch from its controller lets it join another network.");
        Check(net.EvaluateReparent(controller, null, other) == LinkCheck.ControllerConflict,
            "Wiring one controller under another must report ControllerConflict.");
        Check(net.EvaluateReparent(iface, hub, new NodeId(77)) == LinkCheck.UnknownNode,
            "Re-parenting to an unknown node must report UnknownNode.");

        WireKindTests();

        net.Unlink(hub, iface);
        Check(net.GetStatus(hub).State == NodeState.Connected && net.GetStatus(iface).State == NodeState.Disconnected
            && net.GetStatus(terminal).State == NodeState.Disconnected,
            "Cutting the middle link must disconnect downstream nodes.");
        Check(!net.IsConnected(terminal, controller) && !net.IsConnected(iface, controller),
            "Disconnected nodes must fail the host recheck.");
        Check(net.Members(controller).Select(n => n.Value).SequenceEqual(new long[] { 1, 2 }),
            "Members must shrink to the controller's remaining component.");
        Reject(() => net.Unlink(hub, iface), "Unlinking a missing link must throw.");
        Reject(() => net.Unlink(controller, iface), "Unlinking nodes that were never linked must throw.");

        net.RemoveNode(controller);
        Check(net.NodeCount == 4 && net.GetStatus(hub).State == NodeState.Disconnected
            && net.Members(controller).Count == 0,
            "Removing the controller must leave members present but Disconnected.");
        Check(!net.IsConnected(hub, controller), "A removed controller must fail the host recheck.");

        var save = new NetworkRegistry();
        var c1 = new NodeId(10);
        var c2 = new NodeId(11);
        var spine = new NodeId(12);
        save.AddNode(c1, NodeKind.Controller, ControllerTier.Simple);
        save.AddNode(c2, NodeKind.Controller, ControllerTier.Advanced);
        save.AddNode(spine, NodeKind.DriveCabinet);
        save.LoadLink(c1, spine);
        save.LoadLink(c2, spine);
        Check(save.GetStatus(spine).State == NodeState.Conflict && save.GetStatus(c1).State == NodeState.Conflict,
            "Restored multi-controller links must surface as Conflict.");
        Check(save.GetStatus(spine).Controller == null, "Conflicted components must not report a controller.");
        Check(save.Members(c1).Count == 0 && save.Members(c2).Count == 0,
            "Conflicted controllers must report no members.");
        Check(!save.IsConnected(spine, c1) && !save.IsConnected(spine, c2),
            "Conflicted components must fail the host recheck.");
        Check(save.TierOf(c2) == ControllerTier.Advanced, "Tiers must survive a conflicted restore.");

        save.Unlink(c2, spine);
        Check(save.IsConnected(spine, c1) && save.GetStatus(c2).State == NodeState.Connected
            && save.Members(c1).Count == 2,
            "Removing a wire must clear Conflict and restore single-controller service.");

        PositionKeyTests();
    }

    private static void WireKindTests()
    {
        Check(NetworkRegistry.EvaluateWireKinds(NodeKind.Controller, null) == LinkCheck.Ok,
            "A controller may be wired from vanilla power.");
        foreach (var kind in new[] { NodeKind.Hub, NodeKind.Interface, NodeKind.Terminal, NodeKind.DriveCabinet })
            Check(NetworkRegistry.EvaluateWireKinds(kind, null) == LinkCheck.VanillaParent,
                "Non-controller storage blocks must not be wired from vanilla power: " + kind);
        foreach (var kind in new[] { NodeKind.Controller, NodeKind.Hub, NodeKind.Interface })
            Check(NetworkRegistry.EvaluateWireKinds(null, kind) == LinkCheck.VanillaChild,
                "Storage blocks must not feed vanilla devices: " + kind);
        Check(NetworkRegistry.EvaluateWireKinds(NodeKind.Interface, NodeKind.Hub) == LinkCheck.Ok,
            "Storage-to-storage wires must pass the kind rule.");
        Check(NetworkRegistry.EvaluateWireKinds(null, null) == LinkCheck.Ok,
            "Vanilla-to-vanilla wires are not RSC's concern.");
    }

    private static void PositionKeyTests()
    {
        foreach (var pos in new[] { (0, 0, 0), (1, 64, -1), (-300, 128, 12345), (1048575, 255, -1048576), (-1048576, 0, 1048575), (-42, 200, -42) })
        {
            var key = PositionKey.Pack(pos.Item1, pos.Item2, pos.Item3);
            PositionKey.Unpack(key, out int x, out int y, out int z);
            Check(x == pos.Item1 && y == pos.Item2 && z == pos.Item3,
                "PositionKey round trip must be lossless, incl. negative coords.");
        }
        Reject<ArgumentOutOfRangeException>(() => PositionKey.Pack(1048576, 0, 0), "x out of range must throw.");
        Reject<ArgumentOutOfRangeException>(() => PositionKey.Pack(-1048577, 0, 0), "negative x out of range must throw.");
        Reject<ArgumentOutOfRangeException>(() => PositionKey.Pack(0, 0, -1048577), "z out of range must throw.");
        Reject<ArgumentOutOfRangeException>(() => PositionKey.Pack(0, -1, 0), "negative y must throw.");
        Reject<ArgumentOutOfRangeException>(() => PositionKey.Pack(0, 256, 0), "y above 255 must throw.");
    }
}
