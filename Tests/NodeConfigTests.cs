using System;
using System.IO;
using System.Linq;
using RefinedStorageControl.Routing;
using static Harness;

internal static class NodeConfigTests
{
    public static void Run()
    {
        Reject<ArgumentOutOfRangeException>(() => new NodeConfig(101, null), "Priority above 100 must throw.");
        Check(NodeConfig.Default.Priority == 50 && NodeConfig.Default.Filter.Mode == FilterMode.None
            && NodeConfig.Default.IsDefault, "The default config must be priority 50 with no filter.");

        var store = new NodeConfigStore();
        Check(store.Get(7) == NodeConfig.Default && !store.Dirty, "Unknown nodes must read as the default config.");
        store.Set(7, new NodeConfig(10, null));
        Check(store.Get(7).Priority == 10 && store.Count == 1 && store.Dirty, "Set must store and mark dirty.");
        store.Dirty = false;
        store.Set(7, NodeConfig.Default);
        Check(store.Count == 0 && store.Dirty, "Setting the default must drop the entry.");
        store.Dirty = false;
        Check(!store.Remove(7) && !store.Dirty, "Removing a missing node must not mark dirty.");

        var original = new NodeConfigStore();
        original.Set(-5, new NodeConfig(0, new ItemFilter(FilterMode.Allow, new[] { "wood", "resourceScrapIron" })));
        original.Set(42, new NodeConfig(90, null));
        original.Set(3, new NodeConfig(50, new ItemFilter(FilterMode.Block, new[] { "stone" })));
        var loaded = RoundTrip(original);
        Check(loaded.Count == 3 && !loaded.Dirty, "A round trip must keep every node and load clean.");
        Check(loaded.Get(-5).Priority == 0 && loaded.Get(-5).Filter.Mode == FilterMode.Allow
            && loaded.Get(-5).Filter.Accepts("wood") && !loaded.Get(-5).Filter.Accepts("stone"),
            "Allow filters must survive a round trip.");
        Check(loaded.Get(3).Filter.Mode == FilterMode.Block && !loaded.Get(3).Filter.Accepts("stone"),
            "Block filters must survive a round trip.");
        Check(loaded.Get(42).Priority == 90, "Priorities must survive a round trip.");

        var reordered = new NodeConfigStore();
        reordered.Set(42, new NodeConfig(90, null));
        reordered.Set(3, new NodeConfig(50, new ItemFilter(FilterMode.Block, new[] { "stone" })));
        reordered.Set(-5, new NodeConfig(0, new ItemFilter(FilterMode.Allow, new[] { "resourceScrapIron", "wood" })));
        Check(Bytes(original).SequenceEqual(Bytes(reordered)), "Equal stores must serialize to identical bytes.");

        byte[] good = Bytes(original);
        Reject<InvalidDataException>(() => Read(good.Take(good.Length - 3).ToArray()), "Truncated data must be rejected.");
        var badMagic = (byte[])good.Clone();
        badMagic[0] = (byte)'X';
        Reject<InvalidDataException>(() => Read(badMagic), "Bad magic must be rejected.");
        var badVersion = (byte[])good.Clone();
        badVersion[4] = 99;
        Reject<InvalidDataException>(() => Read(badVersion), "Unknown versions must be rejected.");
        var badPriority = (byte[])good.Clone();
        badPriority[12 + 8] = 200; // first node's priority (key -5 sorts first)
        Reject<InvalidDataException>(() => Read(badPriority), "Out-of-range priorities must be rejected.");
        Check(RoundTrip(new NodeConfigStore()).Count == 0, "An empty store must round trip.");
    }

    private static byte[] Bytes(NodeConfigStore store)
    {
        using (var ms = new MemoryStream())
        {
            using (var w = new BinaryWriter(ms))
                NodeConfigSerializer.Write(w, store);
            return ms.ToArray();
        }
    }

    private static NodeConfigStore Read(byte[] bytes)
    {
        using (var r = new BinaryReader(new MemoryStream(bytes)))
            return NodeConfigSerializer.Read(r);
    }

    private static NodeConfigStore RoundTrip(NodeConfigStore store) => Read(Bytes(store));
}
