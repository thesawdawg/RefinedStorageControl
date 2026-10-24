using System;
using HarmonyLib;
using RefinedStorageControl.Core;
using UnityEngine;

namespace RefinedStorageControl.Game.Topology
{
    // The wire tool checks CanHaveParent on the client before sending; on the
    // host (IsServer) the server is authoritative, so the policy applies here.

    [HarmonyPatch(typeof(TileEntityPowered), nameof(TileEntityPowered.CanHaveParent))]
    internal static class CanHaveParentPatch
    {
        private static bool Prefix(TileEntityPowered __instance, IPowered powered, ref bool __result)
        {
            if (!ConnectionManager.Instance.IsServer)
                return true;
            if (!(powered is TileEntityPowered parent))
                return true;
            var eval = RscWirePolicy.Evaluate(__instance.ToWorldPos(), parent.ToWorldPos());
            if (eval.allowed)
                return true;
            RscLog.Warning("wire rejected: " + eval.reason
                + " (" + __instance.ToWorldPos() + " -> " + parent.ToWorldPos() + ")");
            __result = false;
            return false;
        }
    }

    // Remote clients send NetPackageWireActions; the vanilla handler applies
    // SetParent with no CanHaveParent check, so the policy must run here too.
    // Package fields tileEntityPosition (child) and wireChildren[0] (new parent)
    // are public.
    [HarmonyPatch(typeof(NetPackageWireActions), "ProcessPackage")]
    internal static class WireActionsPackagePatch
    {
        private static bool Prefix(NetPackageWireActions __instance, World _world, GameManager _callbacks)
        {
            if (_world == null || !ConnectionManager.Instance.IsServer)
                return true;
            if (__instance.currentOperation != NetPackageWireActions.WireActions.SetParent)
                return true;
            if (__instance.wireChildren == null || __instance.wireChildren.Count == 0)
                return true;
            var eval = RscWirePolicy.Evaluate(__instance.tileEntityPosition, __instance.wireChildren[0]);
            if (eval.allowed)
                return true;
            RscLog.Warning("wire rejected (package): " + eval.reason
                + " (" + __instance.tileEntityPosition + " -> " + __instance.wireChildren[0] + ")");
            // Push authoritative wire state back so the client drops its
            // client-side prediction of the rejected link.
            if (_world.GetTileEntity(__instance.tileEntityPosition) is TileEntityPowered child)
            {
                child.SetModified();
                child.SendWireData();
            }
            var sender = __instance.Sender;
            if (sender != null && _world.GetEntity(sender.entityId) is EntityPlayer player)
                GameManager.ShowTooltipMP(player, Localization.Get(eval.tooltipKey), "ui_denied");
            return false;
        }
    }

    // NET-02: wires drawn from an RSC node to an RSC child get a blue colour
    // when the node's network terminates at an Advanced controller. Tier is
    // resolved client-side by walking parentPosition up through loaded tile
    // entities; anything missing or non-RSC stops the walk uncoloured.
    // Vanilla applies colours inside DrawWires (BuildMesh/TogglePulse), so the
    // postfix must push them to the material itself. Pooled wire nodes keep
    // wireColor across reuse (Reset only restores the pulse colour), so every
    // non-blue wire is forced back to the vanilla black.
    [HarmonyPatch(typeof(TileEntityPowered), nameof(TileEntityPowered.DrawWires))]
    internal static class DrawWiresPatch
    {
        private static readonly Color AdvancedBlue = new Color(0.1f, 0.35f, 1f);
        private static readonly Color AdvancedPulse = new Color(0.45f, 0.75f, 1f);
        private static readonly Color VanillaWire = Color.black;
        private static readonly Color VanillaPulse = Color.yellow;

        private static void Postfix(TileEntityPowered __instance)
        {
            try
            {
                var world = GameManager.Instance.World;
                if (world == null || __instance.currentWireNodes.Count == 0)
                    return;
                var selfPos = __instance.ToWorldPos();
                bool advancedSource = RscBlocks.TryGetRole(world.GetBlock(selfPos).Block, out _, out _)
                    && ResolveControllerTier(__instance, world) == ControllerTier.Advanced;
                bool pulse = PulseShowing(__instance, world, selfPos);
                foreach (var node in __instance.currentWireNodes)
                {
                    if (!(node is FastWireNode wire))
                        continue;
                    bool blue = advancedSource && EndsAtRscNode(__instance, world, wire.GetEndPosition());
                    if (blue)
                    {
                        Apply(wire, AdvancedBlue, AdvancedPulse, pulse);
                    }
                    else if (wire.wireColor != VanillaWire)
                    {
                        // A recycled node that was blue; vanilla special pulse
                        // colours (fence/trip wire) are left untouched.
                        Apply(wire, VanillaWire, wire.pulseColor == AdvancedPulse ? VanillaPulse : wire.pulseColor, pulse);
                    }
                }
            }
            catch (Exception e)
            {
                RscLog.Error("DrawWires colour patch failed: " + e);
            }
        }

        private static void Apply(FastWireNode wire, Color wireColor, Color pulseColor, bool pulse)
        {
            wire.SetWireColor(wireColor);
            wire.SetPulseColor(pulseColor);
            wire.TogglePulse(pulse);
        }

        // Mirrors the flag DrawWires passes to TogglePulse.
        private static bool PulseShowing(TileEntityPowered te, World world, Vector3i selfPos)
        {
            if (!WireManager.Instance.ShowPulse)
                return false;
            return te.wireDataList.Count == 0
                || world.CanPlaceBlockAt(selfPos, world.gameManager.GetPersistentLocalPlayer());
        }

        private static bool EndsAtRscNode(TileEntityPowered te, World world, Vector3 end)
        {
            foreach (var childPos in te.wireDataList)
            {
                if ((childPos.ToVector3() - end).sqrMagnitude < 0.01f)
                    return RscBlocks.TryGetRole(world.GetBlock(childPos).Block, out _, out _);
            }
            return false;
        }

        private static ControllerTier? ResolveControllerTier(TileEntityPowered node, World world)
        {
            var pos = node.ToWorldPos();
            for (int hops = 0; hops < 64; hops++)
            {
                if (!RscBlocks.TryGetRole(world.GetBlock(pos).Block, out var kind, out var tier))
                    return null;
                if (kind == NodeKind.Controller)
                    return tier;
                var te = world.GetTileEntity(pos) as TileEntityPowered;
                if (te == null || te.parentPosition.y == -9999)
                    return null;
                pos = te.parentPosition;
            }
            return null;
        }
    }
}
