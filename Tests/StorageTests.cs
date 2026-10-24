using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RefinedStorageControl.Access;
using RefinedStorageControl.Storage;
using static Harness;

internal static class StorageTests
{
    public static void Run()
    {
        Check(DiskCapacity.Of(DiskTier.K1) == 1_000 && DiskCapacity.Of(DiskTier.K10) == 10_000
            && DiskCapacity.Of(DiskTier.K100) == 100_000 && DiskCapacity.Of(DiskTier.K250) == 250_000
            && DiskCapacity.Of(DiskTier.K500) == 500_000 && DiskCapacity.Of(DiskTier.M1) == 1_000_000,
            "Disk capacities must match the six confirmed tiers.");
        Reject<ArgumentException>(() => DiskCapacity.Of((DiskTier)0), "Undefined disk tiers must be rejected.");
        Reject<ArgumentException>(() => DiskCapacity.Of((DiskTier)99), "Out-of-range disk tiers must be rejected.");

        var keyA = new ItemKey(new byte[] { 1, 2, 3 });
        var keyB = new ItemKey(new byte[] { 1, 2, 3 });
        var keyC = new ItemKey(new byte[] { 1, 2, 4 });
        Check(keyA.Equals(keyB) && keyA == keyB && keyA.GetHashCode() == keyB.GetHashCode(),
            "Equal item bytes must produce equal keys with equal hashes.");
        Check(!keyA.Equals(keyC) && keyA != keyC, "Different item bytes must produce different keys.");
        var source = new byte[] { 9, 9 };
        var copied = new ItemKey(source);
        source[0] = 1;
        Check(copied.ToArray().SequenceEqual(new byte[] { 9, 9 }), "Key construction must copy its source bytes.");
        var roundTrip = copied.ToArray();
        roundTrip[0] = 0;
        Check(copied.ToArray()[0] == 9, "Key byte accessors must return copies.");
        Reject<ArgumentException>(() => new ItemKey(null), "Null item keys must be rejected.");
        Reject<ArgumentException>(() => new ItemKey(Array.Empty<byte>()), "Empty item keys must be rejected.");

        Reject<ArgumentException>(() => new DiskRecord(Guid.Empty, DiskTier.K1, new DigitalAcl(null)),
            "Empty disk ids must be rejected.");
        var disk = new DiskRecord(Guid.NewGuid(), DiskTier.K1, new DigitalAcl(null));
        Check(disk.Capacity == 1_000 && disk.Used == 0 && disk.Free == 1_000, "Fresh disks must be empty at full capacity.");
        Check(disk.Insert(keyA, 1500) == 1_000 && disk.Used == 1_000 && disk.Free == 0 && disk.Count(keyA) == 1_000,
            "Insert must accept up to remaining capacity.");
        Check(disk.Insert(new ItemKey(new byte[] { 2 }), 1) == 0, "Full disks must accept nothing more.");
        Reject<ArgumentException>(() => disk.Insert(keyA, 0), "Zero inserts must be rejected.");
        Reject<ArgumentException>(() => disk.Insert(keyA, -3), "Negative inserts must be rejected.");
        Reject<ArgumentException>(() => disk.Extract(keyA, 0), "Zero extracts must be rejected.");

        var big = new DiskRecord(Guid.NewGuid(), DiskTier.M1, new DigitalAcl(null));
        Check(big.Insert(keyA, 1_000_000) == 1_000_000 && big.Items.Count == 1,
            "A million identical items must stay a single sparse entry.");

        var work = new DiskRecord(Guid.NewGuid(), DiskTier.K1, new DigitalAcl(null));
        work.Insert(keyA, 100);
        Check(work.Extract(keyA, 40) == 40 && work.Count(keyA) == 60 && work.Items.ContainsKey(keyA),
            "Partial extract must reduce the stored count.");
        Check(work.Extract(keyA, 100) == 60 && work.Count(keyA) == 0 && !work.Items.ContainsKey(keyA),
            "Over-extract must release everything and remove the entry.");
        Check(work.Extract(keyA, 10) == 0, "Extracting a missing key must release nothing.");
        var variant1 = new ItemKey(new byte[] { 7, 1 });
        var variant2 = new ItemKey(new byte[] { 7, 2 });
        work.Insert(variant1, 10);
        work.Insert(variant2, 10);
        Check(work.Items.Count == 2 && work.Count(variant1) == 10 && work.Count(variant2) == 10,
            "Different serialized states of the same item must stay distinct entries.");

        SerializerTests();
    }

    private static void SerializerTests()
    {
        var acl = new DigitalAcl("owner-1");
        acl.Set("owner-1", "alice", DigitalPermission.Deposit | DigitalPermission.Withdraw);
        acl.Set("owner-1", "bob", DigitalPermission.Craft);
        var disk = new DiskRecord(Guid.NewGuid(), DiskTier.K10, acl);
        var k1 = new ItemKey(new byte[] { 10 });
        var k2 = new ItemKey(new byte[] { 20, 1 });
        var k3 = new ItemKey(new byte[] { 3, 3, 3 });
        disk.Insert(k1, 100);
        disk.Insert(k2, 5);
        disk.Insert(k3, 42);

        var restored = Read(Serialize(disk));
        Check(restored.Id == disk.Id && restored.Tier == disk.Tier && restored.Capacity == disk.Capacity
            && restored.Used == disk.Used, "Disk fields must round trip.");
        Check(restored.Acl.OwnerId == "owner-1" && restored.Acl.Entries.Count == 2
            && restored.Acl.Entries["alice"] == (DigitalPermission.Deposit | DigitalPermission.Withdraw)
            && restored.Acl.Entries["bob"] == DigitalPermission.Craft, "ACL must round trip.");
        Check(restored.Items.Count == 3 && restored.Count(k1) == 100 && restored.Count(k2) == 5 && restored.Count(k3) == 42,
            "Item entries must round trip.");

        var acl2 = new DigitalAcl("owner-1");
        acl2.Set("owner-1", "bob", DigitalPermission.Craft);
        acl2.Set("owner-1", "alice", DigitalPermission.Deposit | DigitalPermission.Withdraw);
        var reordered = new DiskRecord(disk.Id, DiskTier.K10, acl2);
        reordered.Insert(k3, 42);
        reordered.Insert(k1, 100);
        reordered.Insert(k2, 5);
        Check(Serialize(disk).SequenceEqual(Serialize(reordered)),
            "Identical records must serialize to identical bytes regardless of insertion order.");

        var noAcl = Read(Serialize(new DiskRecord(Guid.NewGuid(), DiskTier.K250, new DigitalAcl(null))));
        Check(!noAcl.Acl.HasOwner && noAcl.Acl.Entries.Count == 0, "Ownerless disks must round trip.");

        RejectData(Craft(w => { w.Write(new byte[] { 0, 1, 2, 3 }); Tail(w); }), "Bad magic must be rejected.");
        RejectData(Craft(w => { Head(w, version: 99); Tail(w); }), "Unknown versions must be rejected.");
        RejectData(Craft(w => { Head(w, version: 0); Tail(w); }), "Version zero must be rejected.");
        RejectData(Craft(w => { Head(w, guid: Guid.Empty.ToByteArray()); Tail(w); }), "Empty disk ids must be rejected.");
        RejectData(Craft(w => { Head(w, tier: 99); Tail(w); }), "Undefined tiers must be rejected.");
        RejectData(Craft(w => { Head(w, tier: 0); Tail(w); }), "Tier zero must be rejected.");
        RejectData(Craft(w => { Head(w); w.Write(-1); w.Write(0); }), "Negative ACL counts must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w, owner: ""); w.Write(1); w.Write("alice"); w.Write(1); w.Write(0);
        }), "Ownerless disks must not carry ACL entries.");
        RejectData(Craft(w =>
        {
            Head(w, owner: "owner-1"); w.Write(1); w.Write(""); w.Write(1); w.Write(0);
        }), "Empty ACL player ids must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w, owner: "owner-1"); w.Write(1); w.Write("owner-1"); w.Write(1); w.Write(0);
        }), "The owner must not appear in the allowlist.");
        RejectData(Craft(w =>
        {
            Head(w);
            w.Write(2); w.Write("dup"); w.Write(1); w.Write("dup"); w.Write(2); w.Write(0);
        }), "Duplicate ACL players must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w);
            w.Write(1); w.Write("alice"); w.Write(16); w.Write(0);
        }), "ACL flags outside defined bits must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w);
            w.Write(1); w.Write("alice"); w.Write(0); w.Write(0);
        }), "Empty ACL flags must be rejected.");
        RejectData(Craft(w => { Head(w); w.Write(0); w.Write(-1); }), "Negative item counts must be rejected.");
        RejectData(Craft(w => { Head(w); w.Write(0); w.Write(1); w.Write(0); w.Write(5L); }),
            "Zero-length keys must be rejected.");
        RejectData(Craft(w => { Head(w); w.Write(0); w.Write(1); w.Write(-4); w.Write(5L); }),
            "Negative key lengths must be rejected.");
        RejectData(Craft(w => { Head(w); w.Write(0); w.Write(1); w.Write(1024 * 1024 + 1); }),
            "Keys over one MiB must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w); w.Write(0); w.Write(1);
            w.Write(3); w.Write(new byte[] { 1, 2, 3 }); w.Write(0L);
        }), "Zero item counts must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w); w.Write(0); w.Write(1);
            w.Write(3); w.Write(new byte[] { 1, 2, 3 }); w.Write(-7L);
        }), "Negative item counts must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w); w.Write(0); w.Write(2);
            w.Write(1); w.Write(new byte[] { 8 }); w.Write(3L);
            w.Write(1); w.Write(new byte[] { 8 }); w.Write(4L);
        }), "Duplicate item keys must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w, tier: (int)DiskTier.K1); w.Write(0); w.Write(2);
            w.Write(1); w.Write(new byte[] { 8 }); w.Write(600L);
            w.Write(1); w.Write(new byte[] { 9 }); w.Write(600L);
        }), "Totals beyond capacity must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w, tier: (int)DiskTier.K1); w.Write(0); w.Write(1);
            w.Write(1); w.Write(new byte[] { 8 }); w.Write(1001L);
        }), "A single item beyond capacity must be rejected.");
        RejectData(Craft(w =>
        {
            Head(w, tier: (int)DiskTier.M1); w.Write(0); w.Write(2);
            w.Write(1); w.Write(new byte[] { 8 }); w.Write(long.MaxValue);
            w.Write(1); w.Write(new byte[] { 9 }); w.Write(long.MaxValue);
        }), "Overflowing counts must be rejected.");

        byte[] valid = Serialize(disk);
        foreach (int cut in new[] { 3, 5, 20, valid.Length / 2, valid.Length - 1 })
        {
            int offset = cut;
            RejectData(() =>
            {
                using (var ms = new MemoryStream(valid, 0, offset))
                using (var r = new BinaryReader(ms))
                    DiskRecordSerializer.Read(r);
            }, "Truncated streams must be rejected at offset " + cut + ".");
        }
    }

    private static byte[] Serialize(DiskRecord disk)
    {
        using (var ms = new MemoryStream())
        {
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                DiskRecordSerializer.Write(w, disk);
            return ms.ToArray();
        }
    }

    private static DiskRecord Read(byte[] bytes)
    {
        using (var r = new BinaryReader(new MemoryStream(bytes)))
            return DiskRecordSerializer.Read(r);
    }

    private static Action Craft(Action<BinaryWriter> fill) => () =>
    {
        using (var ms = new MemoryStream())
        {
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                fill(w);
            using (var r = new BinaryReader(new MemoryStream(ms.ToArray())))
                DiskRecordSerializer.Read(r);
        }
    };

    private static void RejectData(Action action, string message)
    {
        Reject<InvalidDataException>(action, message);
    }

    private static void Head(BinaryWriter w, int version = DiskRecordSerializer.CurrentVersion,
        byte[] guid = null, int tier = (int)DiskTier.K1, string owner = "")
    {
        w.Write(new byte[] { (byte)'R', (byte)'S', (byte)'C', (byte)'D' });
        w.Write(version);
        w.Write(guid ?? Guid.NewGuid().ToByteArray());
        w.Write(tier);
        w.Write(owner);
    }

    private static void Tail(BinaryWriter w)
    {
        w.Write(0);
        w.Write(0);
    }
}
