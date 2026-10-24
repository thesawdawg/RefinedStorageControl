using System;
using System.Collections.Generic;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Topology;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;
using UnityEngine;

namespace RefinedStorageControl.Game.Providers
{
    // Host-authoritative transfers between network storage (containers and, on
    // a powered advanced network, installed disks) and an endpoint grid. Every call runs on the main thread and validates, plans and
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

        internal sealed class Network
        {
            public NodeId Controller;
            public ControllerTier Tier;
            public List<StorageProvider> Providers;
            public List<Vector3i> Interfaces; // fixed position order; stations attach here too (CRAFT-01)
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
                if (p.CanView(user))
                    p.AddEntries(byKey);
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
            var result = DepositCore(net, user, source.Grid, stack.itemValue, stack.count, source.Label + "[" + slot + "]");
            if (result.Ok)
            {
                source.Take(slot, result.Moved);
                source.MarkModified();
            }
            return result;
        }

        // Remote-client deposit (NET packages): the item has already left the
        // client's backpack; the caller returns count - Moved to the client.
        public static Result DepositItem(PlatformUserIdentifierAbs user, Vector3i nodePos, ItemValue item, int count)
        {
            if (!TryResolveNetwork(nodePos, out var net, out string reason))
                return Result.Fail(reason);
            if (item == null || item.IsEmpty() || count <= 0)
                return Result.Fail("nothing to store");
            return DepositCore(net, user, null, item, count, "remote backpack");
        }

        internal static Result DepositCore(Network net, PlatformUserIdentifierAbs user, ItemStackGrid exclude,
            ItemValue item, int count, string from)
        {
            var budget = BudgetFor(net);
            if (budget.Remaining(Now) <= 0)
                return Result.Budget(BudgetReason(budget));
            var key = ItemKeys.FromItemValue(item);
            string itemName = Name(item);
            int maxCount = item.ItemClass?.MaxCount ?? count;
            var candidates = Eligible(net, exclude, p => p.CanDeposit(user), key, holdsCheck: true, out var infos);
            if (candidates.Count == 0)
                return Result.Fail("no storage on the network accepts your items");

            int remaining = count;
            var plans = new List<StorageProvider>();
            foreach (int i in ProviderRouting.DepositOrder(infos, itemName))
            {
                int accepted = candidates[i].PlanDeposit(key, remaining, maxCount);
                if (accepted > 0)
                {
                    plans.Add(candidates[i]);
                    remaining -= accepted;
                }
                if (remaining == 0)
                    break;
            }
            int moved = count - remaining;
            if (moved == 0)
                return Result.Fail("no room on the network for '" + itemName + "'");

            var clone = item.Clone();
            foreach (var provider in plans)
            {
                provider.CommitDeposit(clone);
                provider.MarkModified();
            }
            budget.TryConsume(Now);
            RscLog.Out("deposit " + moved + "x '" + itemName + "' from " + from + " into network "
                + RscBlocks.FromNodeId(net.Controller) + " (" + plans.Count + " providers)");
            return new Result(true, moved, 1, remaining > 0 ? remaining + " did not fit (network full)" : null);
        }

        // Up to one stack of the exact item into the endpoint. Each provider slot
        // drawn from is one transfer; a partial result reports the budget limit.
        public static Result Withdraw(PlatformUserIdentifierAbs user, Vector3i nodePos, ItemKey key, int count, GridEndpoint dest)
        {
            if (!TryResolveNetwork(nodePos, out var net, out string reason))
                return Result.Fail(reason);
            if (!CheckEndpoint(dest, user, out reason))
                return Result.Fail("destination " + reason);
            var result = TakeCore(net, user, dest.Grid, key, count,
                (sample, maxCount, wanted) => wanted - StackPlacement.Plan(key, wanted, maxCount, dest.PlacementSlots(), null),
                out var taken);
            if (!result.Ok)
                return result;
            var adds = new List<StackPlacement.Placement>();
            StackPlacement.Plan(key, result.Moved, taken.ItemClass?.MaxCount ?? result.Moved, dest.PlacementSlots(), adds);
            dest.Apply(adds, taken);
            dest.MarkModified();
            RscLog.Out("withdraw " + result.Moved + "x '" + Name(taken) + "' from network " + RscBlocks.FromNodeId(net.Controller)
                + " into " + dest.Label + " (" + result.Transfers + " transfers)");
            return result;
        }

        // Remote-client withdraw: removes up to `count` (capped at one stack and
        // at what the client reported it can hold) and hands the item back for
        // the caller to ship to the client.
        public static Result TakeItem(PlatformUserIdentifierAbs user, Vector3i nodePos, ItemKey key, int count, out ItemValue taken)
        {
            taken = null;
            if (!TryResolveNetwork(nodePos, out var net, out string reason))
                return Result.Fail(reason);
            var result = TakeCore(net, user, null, key, count, (sample, maxCount, wanted) => wanted, out taken);
            if (result.Ok)
                RscLog.Out("withdraw " + result.Moved + "x '" + Name(taken) + "' from network "
                    + RscBlocks.FromNodeId(net.Controller) + " for a remote player (" + result.Transfers + " transfers)");
            return result;
        }

        private static Result TakeCore(Network net, PlatformUserIdentifierAbs user, ItemStackGrid exclude, ItemKey key,
            int count, Func<ItemValue, int, int, int> roomFor, out ItemValue taken)
        {
            taken = null;
            if (count <= 0)
                return Result.Fail("count must be positive");
            var budget = BudgetFor(net);
            int allowance = budget.Remaining(Now);
            if (allowance <= 0)
                return Result.Budget(BudgetReason(budget));

            var candidates = Eligible(net, exclude, p => p.CanWithdraw(user), key, holdsCheck: false, out var infos);
            ItemValue sample = null;
            var sources = new List<(StorageProvider provider, int handle, int take)>();
            int wanted = count;
            int fit = 0;
            int moved = 0;
            foreach (int i in ProviderRouting.WithdrawOrder(infos))
            {
                var p = candidates[i];
                foreach (var (handle, available) in p.Sources(key))
                {
                    if (sources.Count >= allowance || (sample != null && moved >= fit))
                        break;
                    if (sample == null)
                    {
                        sample = p.SampleAt(handle, key);
                        int maxCount = sample.ItemClass?.MaxCount ?? available;
                        wanted = Math.Min(wanted, maxCount);
                        fit = roomFor(sample, maxCount, wanted);
                        if (fit <= 0)
                            return Result.Fail("destination has no room");
                    }
                    int take = Math.Min(available, fit - moved);
                    sources.Add((p, handle, take));
                    moved += take;
                }
            }
            if (sample == null)
                return Result.Fail("item not available on the network");

            var touched = new HashSet<StorageProvider>();
            foreach (var src in sources)
            {
                src.provider.Take(src.handle, key, src.take);
                touched.Add(src.provider);
            }
            foreach (var provider in touched)
                provider.MarkModified();
            for (int i = 0; i < sources.Count; i++)
                budget.TryConsume(Now);

            taken = sample;
            string note = null;
            if (moved < fit)
                note = sources.Count >= allowance ? "stopped by transfer budget" : "network had only " + moved;
            else if (fit < wanted)
                note = "room for only " + fit;
            return new Result(true, moved, sources.Count, note);
        }

        internal static bool TryResolveNetwork(Vector3i nodePos, out Network net, out string reason)
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
            var interfaces = new List<Vector3i>();
            foreach (var member in registry.Members(controller))
                if (registry.KindOf(member) == NodeKind.Interface)
                    interfaces.Add(RscBlocks.FromNodeId(member));
            net = new Network
            {
                Controller = controller, Tier = tier, Providers = CollectProviders(world, registry, controller, tier),
                Interfaces = interfaces
            };
            reason = null;
            return true;
        }

        // Containers in the six blocks around each interface, deduplicated (a
        // container touching several interfaces takes its settings from the first
        // in fixed position order; Members is sorted), plus every installed disk
        // when the network is advanced (PROG-05: disks need the powered tier;
        // power itself was checked in TryResolveNetwork).
        private static List<StorageProvider> CollectProviders(World world, NetworkRegistry registry, NodeId controller,
            ControllerTier tier)
        {
            var providers = new List<StorageProvider>();
            var seen = new HashSet<Vector3i>();
            var store = Persistence.DiskFile.Store;
            foreach (var member in registry.Members(controller))
            {
                var kind = registry.KindOf(member);
                if (kind == NodeKind.Interface)
                {
                    foreach (var container in ContainersAround(world, RscBlocks.FromNodeId(member)))
                        if (seen.Add(container.Position))
                            providers.Add(new ContainerStorage(container, member));
                }
                else if (kind == NodeKind.DriveCabinet && tier == ControllerTier.Advanced && store != null)
                {
                    var slots = store.Slots(member.Value);
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var disk = slots[i].HasValue ? store.Get(slots[i].Value) : null;
                        if (disk != null)
                            providers.Add(new DiskStorage(store, disk, member, i));
                    }
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

        // Priority and filter come from the serving interface's or cabinet's settings.
        internal static List<StorageProvider> Eligible(Network net, ItemStackGrid exclude, Func<StorageProvider, bool> allowed,
            ItemKey key, bool holdsCheck, out List<ProviderInfo> infos)
        {
            var eligible = new List<StorageProvider>();
            infos = new List<ProviderInfo>();
            foreach (var p in net.Providers)
            {
                if ((exclude != null && ReferenceEquals(p.Grid, exclude)) || p.InUse || !allowed(p))
                    continue;
                eligible.Add(p);
                var config = p.ConfigNode.HasValue
                    ? Persistence.NodeConfigFile.Store.Get(p.ConfigNode.Value.Value)
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
