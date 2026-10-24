using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;
using static Harness;

internal static class StackPlacementTests
{
    public static void Run()
    {
        var key = new ItemKey(new byte[] { 1 });
        var other = new ItemKey(new byte[] { 2 });
        var adds = new List<StackPlacement.Placement>();

        Check(StackPlacement.Plan(key, 5, 5, new[]
        {
            new StackPlacement.Slot(key, 3, 5), new StackPlacement.Slot(key, 4, 5)
        }, adds) == 2 && Adds(adds) == "0:2f,1:1f",
            "Identical keys must top up first in slot order, capped at maxCount.");
        Check(StackPlacement.Plan(key, 10, 5, new[]
        {
            new StackPlacement.Slot(key, 5, 5), new StackPlacement.Slot(null, 0, 0), new StackPlacement.Slot(null, 0, 0)
        }, adds) == 5 && Adds(adds) == "1:5t",
            "Only the first empty slot must receive a new stack.");
        Check(StackPlacement.Plan(key, 10, 5, new[]
        {
            new StackPlacement.Slot(key, 4, 5), new StackPlacement.Slot(null, 0, 0)
        }, adds) == 4 && Adds(adds) == "0:1f,1:5t",
            "Top-ups must precede the new stack and cap at maxCount.");
        Check(StackPlacement.Plan(key, 4, 5, new[]
        {
            new StackPlacement.Slot(other, 1, 5), new StackPlacement.Slot(other, 2, 5)
        }, adds) == 4 && adds.Count == 0,
            "Non-identical item states must never merge.");
        Check(StackPlacement.Plan(key, 7, 5, new[]
        {
            new StackPlacement.Slot(other, 1, 5), new StackPlacement.Slot(null, 0, 0)
        }, adds) == 2 && Adds(adds) == "1:5t",
            "A non-identical slot must be skipped before reaching the empty slot.");
        Check(StackPlacement.Plan(key, 8, 5, new[]
        {
            new StackPlacement.Slot(key, 5, 5), new StackPlacement.Slot(other, 3, 5)
        }, adds) == 8 && adds.Count == 0,
            "A full destination must return the entire stack as remainder.");
        Check(StackPlacement.Plan(key, 8, 5, new StackPlacement.Slot[0], adds) == 8 && adds.Count == 0,
            "An empty destination grid must move nothing.");
        Check(StackPlacement.Plan(null, 8, 5, new[] { new StackPlacement.Slot(null, 0, 0) }, adds) == 8,
            "A null source key must move nothing.");
        Check(StackPlacement.Plan(key, 0, 5, new[] { new StackPlacement.Slot(null, 0, 0) }, adds) == 0 && adds.Count == 0,
            "A zero request must add nothing.");
        Check(StackPlacement.Plan(key, 3, 5, new[]
        {
            new StackPlacement.Slot(null, 0, 0), new StackPlacement.Slot(key, 0, 5)
        }, adds) == 0 && Adds(adds) == "1:3f",
            "An empty identical-key slot must still count as a top-up target.");
        Check(StackPlacement.Plan(key, 4, 5, new[]
        {
            new StackPlacement.Slot(key, 1, 5, locked: true), new StackPlacement.Slot(null, 0, 0, locked: true),
            new StackPlacement.Slot(null, 0, 0)
        }, adds) == 0 && Adds(adds) == "2:4t",
            "Locked slots must never receive items, matching or empty.");

        static string Adds(List<StackPlacement.Placement> list)
        {
            var copy = list.ToList();
            list.Clear();
            return string.Join(",", copy.Select(p => p.Index + ":" + p.Added + (p.IsNewStack ? "t" : "f")));
        }
    }
}
