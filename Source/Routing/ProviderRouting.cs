using System;
using System.Collections.Generic;
using RefinedStorageControl.Core;

namespace RefinedStorageControl.Routing
{
    public enum FilterMode { None, Allow, Block }

    // ROUTE-05: per-interface allow or block list by item name. Filters apply to
    // deposits only; withdrawals see every accessible provider.
    public sealed class ItemFilter
    {
        public static readonly ItemFilter None = new ItemFilter(FilterMode.None, null);

        private readonly HashSet<string> _names;

        public ItemFilter(FilterMode mode, IEnumerable<string> itemNames)
        {
            Mode = mode;
            _names = new HashSet<string>(itemNames ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public FilterMode Mode { get; }

        public IReadOnlyCollection<string> Names => _names;

        public bool Accepts(string itemName)
        {
            switch (Mode)
            {
                case FilterMode.Allow: return itemName != null && _names.Contains(itemName);
                case FilterMode.Block: return itemName == null || !_names.Contains(itemName);
                default: return true;
            }
        }
    }

    public readonly struct ProviderInfo
    {
        public ProviderInfo(long sortKey, int priority, ItemFilter filter, bool holdsItem)
        {
            if (priority < ProviderRouting.MinPriority || priority > ProviderRouting.MaxPriority)
                throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be 0-100.");
            SortKey = sortKey;
            Priority = priority;
            Filter = filter ?? ItemFilter.None;
            HoldsItem = holdsItem;
        }

        public long SortKey { get; } // fixed tie order: packed provider position
        public int Priority { get; }
        public ItemFilter Filter { get; }
        public bool HoldsItem { get; } // already holds the exact item being routed
    }

    // ROUTE-02/03: provider ordering. Callers pass only eligible providers
    // (connected, accessible, not in use, powered where required); the result is
    // a list of indexes into that input.
    public static class ProviderRouting
    {
        public const int MinPriority = 0;
        public const int MaxPriority = 100;
        public const int DefaultPriority = 50;

        // Lowest priority number first; within equal priority a provider already
        // holding the exact item; then fixed position order. Filtered-out
        // providers are omitted.
        public static List<int> DepositOrder(IReadOnlyList<ProviderInfo> providers, string itemName)
        {
            var order = new List<int>();
            for (int i = 0; i < providers.Count; i++)
                if (providers[i].Filter.Accepts(itemName))
                    order.Add(i);
            order.Sort((a, b) =>
            {
                var pa = providers[a];
                var pb = providers[b];
                int c = pa.Priority.CompareTo(pb.Priority);
                if (c != 0) return c;
                c = pb.HoldsItem.CompareTo(pa.HoldsItem);
                return c != 0 ? c : pa.SortKey.CompareTo(pb.SortKey);
            });
            return order;
        }

        // Highest priority number (least preferred) first, then fixed position
        // order. Filters do not apply.
        public static List<int> WithdrawOrder(IReadOnlyList<ProviderInfo> providers)
        {
            var order = new List<int>(providers.Count);
            for (int i = 0; i < providers.Count; i++)
                order.Add(i);
            order.Sort((a, b) =>
            {
                int c = providers[b].Priority.CompareTo(providers[a].Priority);
                return c != 0 ? c : providers[a].SortKey.CompareTo(providers[b].SortKey);
            });
            return order;
        }
    }

    // PROG-04/06 defaults; configurable later through the mod config.
    public static class TransferLimits
    {
        public const int SimpleTransfersPerSecond = 2;
        public const int AdvancedTransfersPerSecond = 20;

        public static int For(ControllerTier tier) =>
            tier == ControllerTier.Advanced ? AdvancedTransfersPerSecond : SimpleTransfersPerSecond;
    }
}
