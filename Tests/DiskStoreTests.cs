using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.Storage;
using static Harness;

internal static class DiskStoreTests
{
    public static void Run()
    {
        Reject<ArgumentException>(() => new DiskStore(Guid.Empty), "A store needs a world id.");
        var store = new DiskStore(Guid.NewGuid());
        Reject<ArgumentException>(() => store.Create(DiskTier.K1, ""), "A new disk needs an owner.");

        var a = store.Create(DiskTier.K1, "ann");
        var b = store.Create(DiskTier.M1, "bob");
        Check(store.DiskCount == 2 && store.Dirty && store.Get(a.Id) == a && a.Acl.OwnerId == "ann",
            "Create must register the disk with its first owner.");
        Check(store.Get(Guid.NewGuid()) == null, "Unknown disk ids must return null (data unavailable).");

        store.Dirty = false;
        Check(store.IsEmpty(100) && store.Slots(100).Length == DiskStore.CabinetSlots,
            "An unknown cabinet is empty with six slots.");
        store.Install(100, 0, a.Id);
        Check(store.Dirty && store.Slots(100)[0] == a.Id && !store.IsEmpty(100), "Install must fill the slot.");
        Reject(() => store.Install(100, 0, b.Id), "An occupied slot must be refused.");
        Reject(() => store.Install(200, 1, a.Id), "A disk must not be installed twice.");
        Reject(() => store.Install(100, 1, Guid.NewGuid()), "Unknown disks cannot be installed.");
        Reject<ArgumentOutOfRangeException>(() => store.Install(100, 6, b.Id), "Slots are 0-5.");
        Check(store.FindCabinet(a.Id).Value == (100L, 0), "FindCabinet must locate an installed disk.");
        Reject(() => store.Destroy(a.Id), "An installed disk must not be destroyed in place.");

        var slotsCopy = store.Slots(100);
        slotsCopy[0] = null;
        Check(store.Slots(100)[0] == a.Id, "Slots must return a copy.");

        store.Install(100, 5, b.Id);
        Check(store.Uninstall(100, 0) == a.Id && store.Uninstall(100, 0) == null,
            "Uninstall must return the disk once.");
        Check(!store.FindCabinet(a.Id).HasValue, "An uninstalled disk is in no cabinet.");

        var loaded = RoundTrip(store);
        Check(loaded.WorldId == store.WorldId && loaded.DiskCount == 2 && !loaded.Dirty,
            "A round trip must keep the world id and every disk, and load clean.");
        Check(loaded.Slots(100)[5] == b.Id && loaded.Get(b.Id).Tier == DiskTier.M1
            && loaded.Get(a.Id).Acl.OwnerId == "ann", "Cabinet slots and disk records must survive a round trip.");
        Check(Bytes(store).SequenceEqual(Bytes(loaded)), "Saving a loaded store must reproduce identical bytes.");

        var removed = store.RemoveCabinet(100);
        Check(removed.SequenceEqual(new[] { b.Id }) && store.IsEmpty(100), "RemoveCabinet must hand back its disks.");
        store.Destroy(b.Id);
        Check(store.Get(b.Id) == null && store.DiskCount == 1, "Destroy must delete the record for good.");

        byte[] good = Bytes(loaded);
        Reject<InvalidDataException>(() => Read(good.Take(good.Length - 5).ToArray()), "Truncated data must be rejected.");
        var badMagic = (byte[])good.Clone();
        badMagic[1] = 0;
        Reject<InvalidDataException>(() => Read(badMagic), "Bad magic must be rejected.");
        Check(RoundTrip(new DiskStore(Guid.NewGuid())).DiskCount == 0, "An empty store must round trip.");

        DestructionTests();
    }

    private static void DestructionTests()
    {
        var disks = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        var rolls = new Queue<double>(new[] { 0.1, 0.5, 0.49, 0.99 });
        var survivors = new System.Collections.Generic.List<Guid>();
        var destroyed = new System.Collections.Generic.List<Guid>();
        DestructionRules.RollDisks(disks, 0.5, () => rolls.Dequeue(), survivors, destroyed);
        Check(survivors.SequenceEqual(new[] { disks[0], disks[2] }) && destroyed.SequenceEqual(new[] { disks[1], disks[3] }),
            "Each disk must be rolled once: below the chance survives, at or above is destroyed.");
        Check(rolls.Count == 0, "Exactly one roll per disk.");

        survivors.Clear();
        destroyed.Clear();
        DestructionRules.RollDisks(disks, 1.0, () => 0.999, survivors, destroyed);
        Check(survivors.Count == 4 && destroyed.Count == 0, "A 100% chance keeps every disk.");
        DestructionRules.RollDisks(disks, 0.0, () => 0.0, survivors, destroyed);
        Check(destroyed.Count == 4, "A 0% chance destroys every disk.");
        Reject<ArgumentOutOfRangeException>(() => DestructionRules.RollDisks(disks, 1.5, () => 0, survivors, destroyed),
            "Chances above 1 must be rejected.");

        Check(DestructionRules.LosesFilter(0.5, () => 0.2) && !DestructionRules.LosesFilter(0.5, () => 0.7),
            "Filter loss must follow its roll.");
        Check(DestructionRules.DiskSurvivalChance == 0.5 && DestructionRules.FilterLossChance == 0.5,
            "Defaults must match the F01 decision (50%).");
    }

    private static byte[] Bytes(DiskStore store)
    {
        using (var ms = new MemoryStream())
        {
            using (var w = new BinaryWriter(ms))
                DiskStoreSerializer.Write(w, store);
            return ms.ToArray();
        }
    }

    private static DiskStore Read(byte[] bytes)
    {
        using (var r = new BinaryReader(new MemoryStream(bytes)))
            return DiskStoreSerializer.Read(r);
    }

    private static DiskStore RoundTrip(DiskStore store) => Read(Bytes(store));
}
