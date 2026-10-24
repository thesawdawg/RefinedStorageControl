using System;
using System.Collections.Generic;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Topology;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;
using UnityEngine;

namespace RefinedStorageControl.Game.Providers
{
    // Host-authoritative transfers between network storage and an endpoint
    // container. Every call runs on the main thread and validates, plans and
    // applies in one step, so no client upload can interleave (A01). The order is:
    // network connected (NET-05) and powered (PWR-02) -> endpoint access/in-use ->
    // budget -> per-provider access/in-use -> exact-key planning -> apply.
    internal static class TransferService
    {
        public readonly struct Result
        {
            public Result(bool ok, int moved, int transfers, string reason, bool budgetLimited = false)
            {
                Ok = ok;
                Moved = moved;
                Transfers = transfers;
                Reason = reason;
                BudgetLimited = budgetLimited;
            }

            public bool Ok { get; }
            public int Moved { get; }
            public int Transfers { get; }
            public string Reason { get; }

            // Nothing was wrong with the request; the transfer budget was used up.
            public bool BudgetLimited { get; }

            public static Result Fail(string reason) => new Result(false, 0, 0, reason);
            public static Result Budget(string reason) => new Result(false, 0, 0, reason, budgetLimited: true);
        }

        public sealed class Entry
        {
            public ItemKey Key;
            public ItemValue Sample;
            public int Count;
            public int Providers;
        }

        private sealed class Network
        {
            public NodeId Controller;
            public ControllerTier Tier;
            public List<ContainerProvider> Providers;
        }

        private static readonly Dictionary<NodeId, TransferBudget> Budgets = new Dictionary<NodeId, TransferBudget>();

        private static double Now => Time.realtimeSinceStartupAsDouble;

        public static void Reset() => Budgets.Clear();

        // ACCESS-02: only providers this user can access are listed.
        public static bool TryList(PlatformUserIdentifierAbs user, Vector3i nodePos, out List<Entry> entries, out string reason)
        {
            entries = null;
            if (!TryResolveNetwork(nodePos, out var net, out reason))
                return false;
            var byKey = new Dictionary<ItemKey, Entry>();
            foreach (var p in net.Providers)
            {
                if (!p.CanAccess(user))
                    continue;
                var seen = new HashSet<ItemKey>();
                for (int i = 0; i < p.Grid.Length; i++)
                {
                    var stack = p.Grid[i];
                    if (GridEndpoint.IsEmpty(stack))
                        continue;
                    var key = ItemKeys.FromItemValue(stack.itemValue);
                    if (!byKey.TryGetValue(key, out var e))
                        byKey[key] = e = new Entry { Key = key, Sample = stack.itemValue.Clone() };
                    e.Count += stack.count;
                    if (seen.Add(key))
                        e.Providers++;
                }
            }
            entries = new List<Entry>(byKey.Values);
            entries.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(Name(a.Sample), Name(b.Sample));
                return c != 0 ? c : a.Sample.Quality.CompareTo(b.Sample.Quality);
            });
            return true;
        }

        // One source slot into the network: one transfer, split across providers
        // in deposit order. Whatever does not fit stays in the source (ROUTE-04).
        public static Result Deposit(PlatformUserIdentifierAbs user, Vector3i nodePos, GridEndpoint source, int slot)
        {
            if (!TryResolveNetwork(nodePos, out var net, out string reason))
                return Result.Fail(reason);
            if (!CheckEndpoint(source, user, out reason))
                return Result.Fail("source " + reason);
            if (slot < 0 || slot >= source.Grid.Length)
                return Result.Fail("slot " + slot + " out of range 0.." + (source.Grid.Length - 1));
            if (source.SlotLocked(slot))
                return Result.Fail("source slot " + slot + " is locked");
            var stack = source.Grid[slot];
            if (GridEndpoint.IsEmpty(stack))
                return Result.Fail("source slot " + slot + " is empty");
            var budget = BudgetFor(net);
            if (budget.Remaining(Now) <= 0)
                return Result.Budget(BudgetReason(budget));

            var key = ItemKeys.FromItemValue(stack.itemValue);
            string itemName = Name(stack.itemValue);
            int maxCount = stack.itemValue.ItemClass?.MaxCount ?? stack.count;
            var candidates = Eligible(net, user, source, key, holdsCheck: true, out var infos);
            if (candidates.Count == 0)
                return Result.Fail("no accessible storage on the network");

            int remaining = stack.count;
            var plans = new List<(ContainerProvider provider, List<StackPlacement.Placement> adds)>();
            foreach (int i in ProviderRouting.DepositOrder(infos, itemName))
            {
                if (remaining == 0)
                    break;
                var adds = new List<StackPlacement.Placement>();
                remaining = StackPlacement.Plan(key, remaining, maxCount, candidates[i].PlacementSlots(), adds);
                if (adds.Count > 0)
                    plans.Add((candidates[i], adds));
            }
            int moved = stack.count - remaining;
            if (moved == 0)
                return Result.Fail("no room on the network for '" + itemName + "'");

            var item = stack.itemValue.Clone();
            foreach (var (provider, adds) in plans)
            {
                provider.Apply(adds, item);
                provider.MarkModified();
            }
            source.Take(slot, moved);
            source.MarkModified();
            budget.TryConsume(Now);
            RscLog.Out("deposit " + moved + "x '" + itemName + "' from " + source.Label + "[" + slot + "] into network "
                + RscBlocks.FromNodeId(net.Controller) + " (" + plans.Count + " providers)");
            return new Result(true, moved, 1, remaining > 0 ? remaining + " left in source (network full)" : null);
        }

        // Up to one stack of the exact item into the endpoint. Each provider slot
        // drawn from is one transfer; a partial result reports the budget limit.
        public static Result Withdraw(PlatformUserIdentifierAbs user, Vector3i nodePos, ItemKey key, int count, GridEndpoint dest)
        {
            if (!TryResolveNetwork(nodePos, out var net, out string reason))
                return Result.Fail(reason);
            if (!CheckEndpoint(dest, user, out reason))
                return Result.Fail("destination " + reason);
            if (count <= 0)
                return Result.Fail("count must be positive");
            var budget = BudgetFor(net);
            int allowance = budget.Remaining(Now);
            if (allowance <= 0)
                return Result.Budget(BudgetReason(budget));

            var candidates = Eligible(net, user, dest, key, holdsCheck: false, out var infos);
            ItemValue sample = null;
            var sources = new List<(ContainerProvider provider, int slot, int take)>();
            int wanted = count;
            int maxCount = 0;
            int fit = 0;
            int moved = 0;
            foreach (int i in ProviderRouting.WithdrawOrder(infos))
            {
                var p = candidates[i];
                for (int s = 0; s < p.Grid.Length; s++)
                {
                    if (sources.Count >= allowance || (sample != null && moved >= fit))
                        break;
                    if (p.SlotLocked(s))
                        continue;
                    var k = p.KeyAt(s);
                    if (!k.HasValue || !k.Value.Equals(key))
                        continue;
                    if (sample == null)
                    {
                        sample = p.Grid[s].itemValue.Clone();
                        maxCount = sample.ItemClass?.MaxCount ?? p.Grid[s].count;
                        wanted = Math.Min(wanted, maxCount);
                        fit = wanted - StackPlacement.Plan(key, wanted, maxCount, dest.PlacementSlots(), null);
                        if (fit == 0)
                            return Result.Fail("destination has no room");
                    }
                    int take = Math.Min(p.Grid[s].count, fit - moved);
                    sources.Add((p, s, take));
                    moved += take;
                }
            }
            if (sample == null)
                return Result.Fail("item not available on the network");

            foreach (var src in sources)
                src.provider.Take(src.slot, src.take);
            var adds = new List<StackPlacement.Placement>();
            StackPlacement.Plan(key, moved, maxCount, dest.PlacementSlots(), adds);
            dest.Apply(adds, sample);
            dest.MarkModified();
            var touched = new HashSet<ContainerProvider>();
            foreach (var src in sources)
                if (touched.Add(src.provider))
                    src.provider.MarkModified();
            for (int i = 0; i < sources.Count; i++)
                budget.TryConsume(Now);

            RscLog.Out("withdraw " + moved + "x '" + Name(sample) + "' from network " + RscBlocks.FromNodeId(net.Controller)
                + " into " + dest.Label + " (" + sources.Count + " transfers)");
            string note = null;
            if (moved < fit)
                note = sources.Count >= allowance ? "stopped by transfer budget" : "network had only " + moved;
            else if (fit < wanted)
                note = "destination had room for only " + fit;
            return new Result(true, moved, sources.Count, note);
        }

        private static bool TryResolveNetwork(Vector3i nodePos, out Network net, out string reason)
        {
            net = null;
            var world = GameManager.Instance.World;
            if (world == null)
            {
                reason = "no world";
                return false;
            }
            if (!RscBlocks.TryGetRole(world.GetBlock(nodePos).Block, out _, out _))
            {
                reason = "no storage network block at " + nodePos;
                return false;
            }
            var registry = ServerTopology.Snapshot();
            var nodeId = RscBlocks.ToNodeId(nodePos);
            // A powered block only joins the wire graph once the wire tool first
            // connects it, so a never-wired block is simply not in the registry.
            if (!registry.Contains(nodeId))
            {
                reason = "not wired to a storage controller";
                return false;
            }
            var status = registry.GetStatus(nodeId);
            if (status.State != NodeState.Connected || !status.Controller.HasValue)
            {
                reason = status.State == NodeState.Conflict
                    ? "network has more than one controller; remove a wire"
                    : "not connected to a controller";
                return false;
            }
            var controller = status.Controller.Value;
            var tier = registry.TierOf(controller) ?? ControllerTier.Simple;
            if (tier == ControllerTier.Advanced)
            {
                var item = ServerTopology.PowerItemAt(RscBlocks.FromNodeId(controller));
                if (item == null || !item.IsPowered)
                {
                    reason = "network has no power";
                    return false;
                }
            }
            net = new Network { Controller = controller, Tier = tier, Providers = CollectProviders(world, registry, controller) };
            reason = null;
            return true;
        }

        // Containers in the six blocks around each interface, deduplicated. A
        // container touching several interfaces takes its settings from the first
        // in fixed position order (Members is sorted).
        public static List<ContainerProvider> CollectProviders(World world, NetworkRegistry registry, NodeId controller)
        {
            var providers = new List<ContainerProvider>();
            var seen = new HashSet<Vector3i>();
            foreach (var member in registry.Members(controller))
            {
                if (registry.KindOf(member) != NodeKind.Interface)
                    continue;
                foreach (var provider in ContainersAround(world, RscBlocks.FromNodeId(member)))
                {
                    if (!seen.Add(provider.Position))
                        continue;
                    provider.Interface = member;
                    providers.Add(provider);
                }
            }
            return providers;
        }

        public static List<ContainerProvider> ContainersAround(World world, Vector3i interfacePos)
        {
            var found = new List<ContainerProvider>();
            foreach (var dir in Neighbors)
            {
                var np = interfacePos + dir;
                if (RscBlocks.TryGetRole(world.GetBlock(np).Block, out _, out _))
                    continue;
                if (ContainerProvider.TryResolve(world, np, out var provider, out _)
                    && !found.Exists(f => f.Position.Equals(provider.Position)))
                    found.Add(provider);
            }
            return found;
        }

        private static readonly Vector3i[] Neighbors =
        {
            new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 1, 0),
            new Vector3i(0, -1, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1)
        };

        // Priority and filter come from the serving interface's saved settings.
        private static List<ContainerProvider> Eligible(Network net, PlatformUserIdentifierAbs user, GridEndpoint endpoint,
            ItemKey key, bool holdsCheck, out List<ProviderInfo> infos)
        {
            var eligible = new List<ContainerProvider>();
            infos = new List<ProviderInfo>();
            foreach (var p in net.Providers)
            {
                if (ReferenceEquals(p.Grid, endpoint.Grid) || !p.CanAccess(user) || p.InUse)
                    continue;
                eligible.Add(p);
                var config = p.Interface.HasValue
                    ? Persistence.NodeConfigFile.Store.Get(p.Interface.Value.Value)
                    : NodeConfig.Default;
                infos.Add(new ProviderInfo(p.SortKey, config.Priority, config.Filter, holdsCheck && p.Holds(key)));
            }
            return eligible;
        }

        private static bool CheckEndpoint(GridEndpoint endpoint, PlatformUserIdentifierAbs user, out string reason)
        {
            if (!endpoint.CanAccess(user))
                reason = "access denied (locked, jammed or needs lockpicking)";
            else if (endpoint.InUse)
                reason = "is in use by a player";
            else
            {
                reason = null;
                return true;
            }
            return false;
        }

        private static TransferBudget BudgetFor(Network net)
        {
            int rate = TransferLimits.For(net.Tier);
            if (!Budgets.TryGetValue(net.Controller, out var budget) || budget.TransfersPerSecond != rate)
                Budgets[net.Controller] = budget = new TransferBudget(rate);
            return budget;
        }

        private static string BudgetReason(TransferBudget budget) =>
            "transfer budget used (" + budget.TransfersPerSecond + "/s); try again in "
            + Math.Max(0, budget.NextAvailableAt(Now) - Now).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s";

        public static string Name(ItemValue value) => value?.ItemClass?.GetItemName() ?? "?";
    }
}
