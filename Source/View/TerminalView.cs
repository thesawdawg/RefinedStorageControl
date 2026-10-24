using System;
using System.Collections.Generic;
using System.Linq;

namespace RefinedStorageControl.View
{
    // UI-02: terminal search, categories, sorting and favorites, as pure logic.
    // The game layer describes each entry with a ViewItem and gets back the
    // order to show them in.

    public readonly struct ViewItem
    {
        public ViewItem(string displayName, string itemName, int count, int quality, IReadOnlyList<string> groups)
        {
            DisplayName = displayName ?? "";
            ItemName = itemName ?? "";
            Count = count;
            Quality = quality;
            Groups = groups ?? Array.Empty<string>();
        }

        public string DisplayName { get; }
        public string ItemName { get; }
        public int Count { get; }
        public int Quality { get; }
        public IReadOnlyList<string> Groups { get; }
    }

    public enum SortMode { Name, Count, Quality }

    public sealed class Category
    {
        public Category(string key, string label, params string[] groups)
        {
            Key = key;
            Label = label;
            Groups = new HashSet<string>(groups, StringComparer.Ordinal);
        }

        public string Key { get; }
        public string Label { get; }
        public HashSet<string> Groups { get; }
    }

    public static class TerminalView
    {
        public const string AllKey = "all";
        public const string OtherKey = "other";

        // Vanilla item "Group" values mapped to player-facing categories. Groups
        // prefixed TC/CF/adv are crafting-menu variants of the same families.
        public static readonly IReadOnlyList<Category> Categories = new[]
        {
            new Category(AllKey, "All"),
            new Category("resources", "Resources", "Resources", "Chemicals", "CFChemicals", "Basics"),
            new Category("weapons", "Weapons & Ammo", "Ammo/Weapons", "Ammo", "Ranged Weapons", "Melee Weapons"),
            new Category("tools", "Tools", "Tools/Traps"),
            new Category("food", "Food & Medical", "Food/Cooking", "CFFood/Cooking", "CFDrink/Cooking",
                "Medical", "CFMedical", "TCMedical"),
            new Category("armor", "Armor & Clothing", "Armor", "Clothing", "TCArmor"),
            new Category("building", "Building", "Building", "advBuilding", "Decor/Miscellaneous"),
            new Category("science", "Science & Books", "Science", "TCScience", "Books", "BooksOnly", "TCReading",
                "Robotics", "SchematicsOnly"),
            new Category(OtherKey, "Other"),
        };

        public static Category Find(string key) =>
            Categories.FirstOrDefault(c => c.Key == key) ?? Categories[0];

        // First matching category in list order; unmatched items are "other".
        public static string CategoryOf(ViewItem item)
        {
            foreach (var category in Categories)
            {
                if (category.Key == AllKey || category.Key == OtherKey)
                    continue;
                foreach (var group in item.Groups)
                    if (category.Groups.Contains(group))
                        return category.Key;
            }
            return OtherKey;
        }

        // Next category after `current` that has at least one item, wrapping to
        // "all". Empty categories are skipped so the button never shows nothing.
        public static string NextCategory(string current, IEnumerable<ViewItem> items)
        {
            var present = new HashSet<string>(items.Select(CategoryOf));
            int start = Math.Max(0, Categories.ToList().FindIndex(c => c.Key == current));
            for (int step = 1; step <= Categories.Count; step++)
            {
                var candidate = Categories[(start + step) % Categories.Count];
                if (candidate.Key == AllKey || present.Contains(candidate.Key))
                    return candidate.Key;
            }
            return AllKey;
        }

        public static SortMode NextSort(SortMode mode) => (SortMode)(((int)mode + 1) % 3);

        // Indexes into `items` to show: matching search and category, favorites
        // first, then by the chosen sort. Ties fall back to name then quality so
        // the order never jumps between refreshes.
        public static List<int> Arrange(IReadOnlyList<ViewItem> items, string search, string categoryKey,
            SortMode sort, ICollection<string> favorites)
        {
            string term = (search ?? "").Trim();
            var result = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (term.Length > 0
                    && item.DisplayName.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) < 0
                    && item.ItemName.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (categoryKey != null && categoryKey != AllKey && CategoryOf(item) != categoryKey)
                    continue;
                result.Add(i);
            }
            result.Sort((ia, ib) =>
            {
                var a = items[ia];
                var b = items[ib];
                bool fa = favorites != null && favorites.Contains(a.ItemName);
                bool fb = favorites != null && favorites.Contains(b.ItemName);
                if (fa != fb)
                    return fa ? -1 : 1;
                int c = 0;
                switch (sort)
                {
                    case SortMode.Count: c = b.Count.CompareTo(a.Count); break;
                    case SortMode.Quality: c = b.Quality.CompareTo(a.Quality); break;
                }
                if (c != 0)
                    return c;
                c = string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0)
                    return c;
                c = b.Quality.CompareTo(a.Quality);
                return c != 0 ? c : ia.CompareTo(ib);
            });
            return result;
        }
    }
}
