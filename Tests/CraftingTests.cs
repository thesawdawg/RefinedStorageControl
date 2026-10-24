using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RefinedStorageControl.Crafting;
using RefinedStorageControl.Storage;
using static Harness;

internal static class CraftingTests
{
    private static readonly ItemKey IronA = new ItemKey(new byte[] { 1, 1 });
    private static readonly ItemKey IronB = new ItemKey(new byte[] { 1, 2 });
    private static readonly ItemKey Wood = new ItemKey(new byte[] { 2, 1 });
    private const int IronType = 10;
    private const int WoodType = 20;

    public static void Run()
    {
        Planner();
        Stations();
        Pending();
        Tags();
        Store();
    }

    private static void Planner()
    {
        var supplies = new List<Supply> { new Supply(IronA, IronType, 3), new Supply(IronB, IronType, 8), new Supply(Wood, WoodType, 5) };
        var needs = new List<Need> { new Need(IronType, 10), new Need(WoodType, 2) };
        var plan = IngredientPlanner.Plan(needs, supplies, k => 0, out _, out _);
        Check(plan != null && plan[IronB] == 8 && plan[IronA] == 2 && plan[Wood] == 2,
            "The planner must draw the larger supply first and cover every need.");
        Check(IngredientPlanner.Plan(needs, supplies, k => 0, out _, out _).SequenceEqual(plan),
            "The same storage must always give the same plan.");

        var shortPlan = IngredientPlanner.Plan(new List<Need> { new Need(IronType, 12) }, supplies, k => 0, out int shortType, out int shortBy);
        Check(shortPlan == null && shortType == IronType && shortBy == 1, "A short need must name the type and amount missing.");

        var reservedPlan = IngredientPlanner.Plan(new List<Need> { new Need(IronType, 10) }, supplies,
            k => k.Equals(IronB) ? 6 : 0, out _, out shortBy);
        Check(reservedPlan == null && shortBy == 5, "Items reserved by other requests must not be planned again (CRAFT-06).");

        var split = new List<Need> { new Need(IronType, 4), new Need(IronType, 4) };
        var merged = IngredientPlanner.Plan(split, supplies, k => 0, out _, out _);
        Check(merged != null && merged.Values.Sum() == 8, "Repeated ingredient types must be merged, not double counted.");

        var perCraft = new List<Need> { new Need(IronType, 2), new Need(WoodType, 1) };
        Check(IngredientPlanner.MaxCrafts(perCraft, supplies, k => 0, 1000) == 5, "Max crafts is limited by the scarcest ingredient.");
        Check(IngredientPlanner.MaxCrafts(perCraft, supplies, k => 0, 3) == 3, "Max crafts must respect the cap.");
        Check(IngredientPlanner.MaxCrafts(new List<Need> { new Need(99, 1) }, supplies, k => 0, 10) == 0, "A missing type allows no crafts.");
        Check(IngredientPlanner.MaxCrafts(new List<Need> { new Need(IronType, 0) }, supplies, k => 0, 10) == 10,
            "An ingredient reduced to zero by perks costs nothing.");
        Check(IngredientPlanner.Scale(perCraft, 3)[0].Count == 6, "Scaling multiplies per-craft needs.");
        Reject<OverflowException>(() => IngredientPlanner.Scale(new List<Need> { new Need(1, int.MaxValue) }, 2),
            "Scaling must not overflow silently.");
    }

    private static void Stations()
    {
        var candidates = new List<StationCandidate>
        {
            new StationCandidate(50, true, 1, 30f),
            new StationCandidate(10, true, 2, 30f),
            new StationCandidate(5, false, 4, 0f),
            new StationCandidate(1, true, 0, 0f)
        };
        Check(StationChooser.Choose(candidates) == 1, "Ties on queue time must go to the lower position key.");
        candidates.Add(new StationCandidate(99, true, 1, 12f));
        Check(StationChooser.Choose(candidates) == 4, "The shortest estimated queue must win.");
        Check(StationChooser.Choose(new List<StationCandidate> { new StationCandidate(1, false, 3, 0f) }) == -1,
            "No usable station means no choice.");

        Check(QueueSlots.AppendIndex(new[] { false, false, false, false }) == 3, "An empty queue starts at the crafting slot.");
        Check(QueueSlots.AppendIndex(new[] { false, false, true, true }) == 1, "A new job goes right behind the queued ones.");
        Check(QueueSlots.AppendIndex(new[] { true, true, true, true }) == -1, "A full queue has no slot.");
        Check(QueueSlots.Free(new[] { false, true, false, true }) == 1,
            "Slots ahead of a queued job never count as free (no reordering).");

        Check(ClaimArea.Contains(100, 100, 41, 120, 80) && !ClaimArea.Contains(100, 100, 41, 121, 100),
            "A 41-block claim reaches 20 blocks from its centre.");
        Check(ClaimArea.ObserverRadius(41) == 3, "A 41-block claim needs a 3-chunk observer radius.");
    }

    private static void Pending()
    {
        var pending = new PendingRequests();
        pending.Add(new PendingRequest(2, "p1", 7, 100, 5.0, new Dictionary<ItemKey, int> { [IronA] = 3 }, null));
        pending.Add(new PendingRequest(1, "p2", 7, 200, 6.0, new Dictionary<ItemKey, int> { [IronA] = 2 },
            new Dictionary<int, int> { [IronType] = 4 }));
        pending.Add(new PendingRequest(3, "p1", 8, 100, 9.0, new Dictionary<ItemKey, int> { [IronA] = 50 }, null));
        Reject(() => pending.Add(new PendingRequest(2, "p1", 7, 100, 5.0, null, null)), "Duplicate request ids must be rejected.");

        Check(pending.ReservedInput(7, IronA) == 5, "Reservations add up per network.");
        Check(pending.ReservedInput(7, IronA, exceptId: 2) == 2, "A request re-planning at dispatch ignores its own reservation.");
        Check(pending.ReservedInput(8, IronA) == 50, "Reservations on another network are separate.");
        Check(pending.ReservedMaterial(200, IronType) == 4 && pending.ReservedMaterial(100, IronType) == 0,
            "Station material reservations are per station.");
        Check(pending.ReservedSlots(100) == 2, "Each pending request holds one queue slot at its station.");

        var due = pending.Due(6.0);
        Check(due.Count == 2 && due[0].Id == 1 && due[1].Id == 2, "Due requests come oldest id first.");
        Check(pending.For("p1").Select(r => r.Id).SequenceEqual(new[] { 2, 3 }), "Requests are listed per requester.");
        Check(pending.Remove(2) && !pending.Remove(2) && pending.ReservedInput(7, IronA) == 2,
            "Cancelling releases the reservation exactly once (CRAFT-04).");
    }

    private static void Tags()
    {
        Check(CraftTags.ToTag(1) == -1000001, "Job 1 maps to its tag.");
        Check(CraftTags.TryFromTag(CraftTags.ToTag(12345), out int id) && id == 12345, "Tags round trip.");
        Check(!CraftTags.IsTag(-1) && !CraftTags.IsTag(171) && !CraftTags.IsTag(-1000000), "Player ids and -1 are not tags.");
        Check(CraftTags.IsTag(CraftTags.ToTag(CraftTags.MaxId - 1)) && !CraftTags.IsTag(int.MinValue),
            "The tag range is bounded on both ends.");
        Reject<ArgumentOutOfRangeException>(() => CraftTags.ToTag(0), "Job ids start at 1.");
    }

    private static void Store()
    {
        var store = new CraftJobStore();
        int a = store.NewId();
        int b = store.NewId();
        Check(a == 1 && b == 2 && store.Dirty, "Job ids count up and mark the store dirty.");
        store.Add(new CraftJob
        {
            Id = a, Requester = "Steam_1", RequesterEntity = 171, Station = 99, Node = 98, Controller = 97, Claim = 96,
            Recipe = "resourceForgedIron", OutputItem = "resourceForgedIron", OutputQuality = 1, Crafts = 10, Completed = 4, Owed = 4
        });
        store.Add(new CraftJob { Id = b, Requester = "Steam_2", Recipe = "gunPistol", OutputItem = "gunPistol", OutputQuality = 3, Crafts = 1, QueueDone = true });
        Reject(() => store.Add(new CraftJob { Id = a }), "Duplicate jobs must be rejected.");

        store.Bank("Steam_1", new XpGrant { Recipe = "resourceForgedIron", Item = "resourceForgedIron", Exp = 5, Count = 1 });
        store.Bank("Steam_1", new XpGrant { Recipe = "resourceForgedIron", Item = "resourceForgedIron", Exp = 5, Count = 1 });
        store.Bank("Steam_1", new XpGrant { Recipe = "gunPistol", Item = "gunPistol", Exp = 30, Count = 1 });
        var banked = store.Banked("Steam_1");
        Check(banked.Count == 2 && banked[0].Count == 2 && banked[0].Exp == 5, "Banked XP merges like the native station does.");

        var loaded = RoundTrip(store);
        var j = loaded.Get(a);
        Check(loaded.Count == 2 && !loaded.Dirty && loaded.NextId == 3, "A round trip keeps every job, the id counter, and loads clean.");
        Check(j.Requester == "Steam_1" && j.RequesterEntity == 171 && j.Claim == 96 && j.Owed == 4 && j.Completed == 4
            && j.OutputQuality == 1 && !j.QueueDone, "Job fields survive a round trip.");
        Check(loaded.Get(b).Claim == null && loaded.Get(b).QueueDone, "A job without a claim stays without one.");
        Check(loaded.Banked("Steam_1").Count == 2 && loaded.Banked("Steam_2").Count == 0, "Banked XP survives a round trip.");
        Check(Bytes(store).SequenceEqual(Bytes(loaded)), "Equal stores serialize to identical bytes.");

        loaded.ClearBank("Steam_1");
        Check(loaded.Banked("Steam_1").Count == 0 && loaded.Dirty, "Granted XP leaves the bank.");
        Check(loaded.Remove(a) && loaded.Get(a) == null, "Finished jobs are removed.");
        loaded.Add(new CraftJob { Id = 3 });
        Check(loaded.NewId() == 4, "New ids skip ids still in use.");

        byte[] good = Bytes(store);
        Reject<InvalidDataException>(() => Read(good.Take(good.Length - 2).ToArray()), "Truncated job data must be rejected.");
        var bad = (byte[])good.Clone();
        bad[0] = (byte)'X';
        Reject<InvalidDataException>(() => Read(bad), "Bad magic must be rejected.");
        Reject<InvalidDataException>(() => Read(good.Concat(new byte[] { 0 }).ToArray()), "Trailing bytes must be rejected.");
        Check(RoundTrip(new CraftJobStore()).Count == 0, "An empty store must round trip.");
    }

    private static byte[] Bytes(CraftJobStore store)
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            CraftJobSerializer.Write(w, store);
            w.Flush();
            return ms.ToArray();
        }
    }

    private static CraftJobStore Read(byte[] bytes)
    {
        using (var r = new BinaryReader(new MemoryStream(bytes)))
            return CraftJobSerializer.Read(r);
    }

    private static CraftJobStore RoundTrip(CraftJobStore store) => Read(Bytes(store));
}
