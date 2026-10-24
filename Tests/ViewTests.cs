using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.View;
using static Harness;

internal static class ViewTests
{
    public static void Run()
    {
        var items = new[]
        {
            new ViewItem("Wood", "resourceWood", 500, 0, new[] { "Resources" }),          // 0
            new ViewItem("Pipe Pistol", "gunHandgunT0PipePistol", 1, 3, new[] { "Ammo/Weapons", "Ranged Weapons" }), // 1
            new ViewItem("Pipe Pistol", "gunHandgunT0PipePistol", 1, 5, new[] { "Ammo/Weapons" }), // 2
            new ViewItem("Stone", "resourceRockSmall", 900, 0, new[] { "Resources" }),    // 3
            new ViewItem("Canned Chili", "foodCanChili", 4, 0, new[] { "CFFood/Cooking" }), // 4
            new ViewItem("Odd Thing", "oddThing", 2, 0, new[] { "Limited" }),             // 5
            new ViewItem("Stone Axe", "meleeToolAxeT0StoneAxe", 1, 2, new[] { "Tools/Traps" }), // 6
        };

        Check(TerminalView.CategoryOf(items[0]) == "resources" && TerminalView.CategoryOf(items[1]) == "weapons"
            && TerminalView.CategoryOf(items[4]) == "food" && TerminalView.CategoryOf(items[5]) == "other",
            "Vanilla groups (including crafting-menu variants) must map to categories; unknown groups are Other.");
        Check(TerminalView.Find("nope").Key == "all", "Unknown category keys must fall back to All.");

        Check(Order(items, "", "all", SortMode.Name, null) == "4,5,2,1,3,6,0",
            "Name sort: alphabetical, higher quality first among equal names.");
        Check(Order(items, "", "all", SortMode.Count, null) == "3,0,4,5,2,1,6",
            "Count sort: largest first, then name and quality.");
        Check(Order(items, "", "all", SortMode.Quality, null) == "2,1,6,4,5,3,0",
            "Quality sort: highest quality first, then name.");
        Check(Order(items, "sto", "all", SortMode.Name, null) == "2,1,3,6",
            "Search must match display or internal names case-insensitively (Pistol contains sto).");
        Check(Order(items, "rockSMALL", "all", SortMode.Name, null) == "3",
            "Search must also match internal item names.");
        Check(Order(items, "", "resources", SortMode.Name, null) == "3,0",
            "Category filtering must keep only that category.");
        Check(Order(items, "", "all", SortMode.Name, new HashSet<string> { "resourceWood" }) == "0,4,5,2,1,3,6",
            "Favorites must come first whatever the sort.");
        Check(Order(items, "", "all", SortMode.Count, new HashSet<string> { "gunHandgunT0PipePistol" }) == "2,1,3,0,4,5,6",
            "Favorites apply to every variant of the item, sorted among themselves.");

        Check(TerminalView.NextCategory("all", items) == "resources", "Next category must skip to the first non-empty one.");
        Check(TerminalView.NextCategory("tools", items) == "food", "Empty categories (armor) must be skipped.");
        Check(TerminalView.NextCategory("other", items) == "all", "Cycling must wrap back to All.");
        Check(TerminalView.NextCategory("all", new ViewItem[0]) == "all", "With no items the cycle stays on All.");
        Check(TerminalView.NextSort(SortMode.Name) == SortMode.Count && TerminalView.NextSort(SortMode.Quality) == SortMode.Name,
            "Sort modes must cycle Name, Count, Quality.");
    }

    private static string Order(IReadOnlyList<ViewItem> items, string search, string category, SortMode sort,
        ICollection<string> favorites) =>
        string.Join(",", TerminalView.Arrange(items, search, category, sort, favorites));
}
