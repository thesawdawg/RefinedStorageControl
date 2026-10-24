using System;
using System.Collections.Generic;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Crafting
{
    // An item type a request needs, and how many for the whole request.
    public readonly struct Need
    {
        public Need(int type, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            Type = type;
            Count = count;
        }

        public int Type { get; }
        public int Count { get; }
    }

    // One exact item state the requester may draw from, its type, and how many.
    public readonly struct Supply
    {
        public Supply(ItemKey key, int type, int count)
        {
            Key = key;
            Type = type;
            Count = count;
        }

        public ItemKey Key { get; }
        public int Type { get; }
        public int Count { get; }
    }

    // Chooses which exact item states pay for a request (CRAFT-02/06). Native
    // recipes name item types, while storage holds exact states, so any state
    // of the right type counts. Amounts other pending requests have reserved
    // are not available. Larger supplies are drawn first, ties in key order, so
    // the same storage always gives the same plan.
    public static class IngredientPlanner
    {
        public static Dictionary<ItemKey, int> Plan(IReadOnlyList<Need> needs, IReadOnlyList<Supply> supplies,
            Func<ItemKey, int> reserved, out int shortType, out int shortBy)
        {
            if (needs == null)
                throw new ArgumentNullException(nameof(needs));
            var free = FreeByType(supplies, reserved);
            var plan = new Dictionary<ItemKey, int>();
            foreach (var need in Merge(needs))
            {
                int left = need.Count;
                if (free.TryGetValue(need.Type, out var list))
                    foreach (var (key, count) in list)
                    {
                        if (left == 0)
                            break;
                        int take = Math.Min(left, count);
                        if (take <= 0)
                            continue;
                        plan.TryGetValue(key, out int already);
                        plan[key] = already + take;
                        left -= take;
                    }
                if (left > 0)
                {
                    shortType = need.Type;
                    shortBy = left;
                    return null;
                }
            }
            shortType = 0;
            shortBy = 0;
            return plan;
        }

        // How many whole crafts the supplies cover, up to `cap`.
        public static int MaxCrafts(IReadOnlyList<Need> perCraft, IReadOnlyList<Supply> supplies,
            Func<ItemKey, int> reserved, int cap)
        {
            if (perCraft == null)
                throw new ArgumentNullException(nameof(perCraft));
            var totals = new Dictionary<int, long>();
            foreach (var pair in FreeByType(supplies, reserved))
            {
                long sum = 0;
                foreach (var (_, count) in pair.Value)
                    sum += count;
                totals[pair.Key] = sum;
            }
            long max = cap;
            foreach (var need in Merge(perCraft))
            {
                if (need.Count == 0)
                    continue;
                totals.TryGetValue(need.Type, out long have);
                max = Math.Min(max, have / need.Count);
            }
            return (int)Math.Max(0, max);
        }

        public static List<Need> Scale(IReadOnlyList<Need> perCraft, int crafts)
        {
            var scaled = new List<Need>(perCraft.Count);
            foreach (var need in perCraft)
                scaled.Add(new Need(need.Type, checked(need.Count * crafts)));
            return scaled;
        }

        private static List<Need> Merge(IReadOnlyList<Need> needs)
        {
            var order = new List<int>();
            var sums = new Dictionary<int, int>();
            foreach (var need in needs)
            {
                if (!sums.ContainsKey(need.Type))
                {
                    order.Add(need.Type);
                    sums[need.Type] = 0;
                }
                sums[need.Type] = checked(sums[need.Type] + need.Count);
            }
            var merged = new List<Need>(order.Count);
            foreach (int type in order)
                merged.Add(new Need(type, sums[type]));
            return merged;
        }

        private static Dictionary<int, List<(ItemKey key, int count)>> FreeByType(IReadOnlyList<Supply> supplies,
            Func<ItemKey, int> reserved)
        {
            var byKey = new Dictionary<ItemKey, (int type, long count)>();
            if (supplies != null)
                foreach (var s in supplies)
                {
                    byKey.TryGetValue(s.Key, out var current);
                    byKey[s.Key] = (s.Type, current.count + Math.Max(0, s.Count));
                }
            var byType = new Dictionary<int, List<(ItemKey, int)>>();
            foreach (var pair in byKey)
            {
                long free = pair.Value.count - Math.Max(0, reserved?.Invoke(pair.Key) ?? 0);
                if (free <= 0)
                    continue;
                if (!byType.TryGetValue(pair.Value.type, out var list))
                    byType[pair.Value.type] = list = new List<(ItemKey, int)>();
                list.Add((pair.Key, (int)Math.Min(free, int.MaxValue)));
            }
            foreach (var list in byType.Values)
                list.Sort((a, b) =>
                {
                    int c = b.Item2.CompareTo(a.Item2);
                    return c != 0 ? c : CompareBytes(a.Item1.RawBytes, b.Item1.RawBytes);
                });
            return byType;
        }

        private static int CompareBytes(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }
    }
}
