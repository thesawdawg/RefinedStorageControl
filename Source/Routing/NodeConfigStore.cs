using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RefinedStorageControl.Routing
{
    // Per-node settings that vanilla tile entities cannot hold (P0 finding 2):
    // interface priority (ROUTE-02) and deposit filter (ROUTE-05), keyed by the
    // packed block position. A node with default settings has no entry.
    public sealed class NodeConfig
    {
        public NodeConfig(int priority, ItemFilter filter)
        {
            if (priority < ProviderRouting.MinPriority || priority > ProviderRouting.MaxPriority)
                throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be 0-100.");
            Priority = priority;
            Filter = filter ?? ItemFilter.None;
        }

        public static readonly NodeConfig Default = new NodeConfig(ProviderRouting.DefaultPriority, ItemFilter.None);

        public int Priority { get; }
        public ItemFilter Filter { get; }

        public bool IsDefault => Priority == ProviderRouting.DefaultPriority && Filter.Mode == FilterMode.None;
    }

    public sealed class NodeConfigStore
    {
        private readonly Dictionary<long, NodeConfig> _configs = new Dictionary<long, NodeConfig>();

        public int Count => _configs.Count;

        // Set when settings change; the game layer clears it after a save.
        public bool Dirty { get; set; }

        public NodeConfig Get(long nodeKey) =>
            _configs.TryGetValue(nodeKey, out var config) ? config : NodeConfig.Default;

        public void Set(long nodeKey, NodeConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (config.IsDefault)
            {
                Remove(nodeKey);
                return;
            }
            _configs[nodeKey] = config;
            Dirty = true;
        }

        public bool Remove(long nodeKey)
        {
            if (!_configs.Remove(nodeKey))
                return false;
            Dirty = true;
            return true;
        }

        public IEnumerable<KeyValuePair<long, NodeConfig>> Entries => _configs;
    }

    // Binary format v1, little-endian via BinaryWriter/BinaryReader:
    //   "RSCN" magic, int32 version, int32 count,
    //   per node { int64 key, int32 priority, int32 filterMode,
    //              int32 nameCount, per name { string } }.
    // Nodes and names are written sorted, so equal stores give identical bytes.
    public static class NodeConfigSerializer
    {
        public const int CurrentVersion = 1;

        private const int MaxNodes = 1 << 20;
        private const int MaxNames = 4096;
        private static readonly byte[] Magic = { (byte)'R', (byte)'S', (byte)'C', (byte)'N' };

        public static void Write(BinaryWriter writer, NodeConfigStore store)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            var entries = store.Entries.OrderBy(e => e.Key).ToList();
            writer.Write(Magic);
            writer.Write(CurrentVersion);
            writer.Write(entries.Count);
            foreach (var entry in entries)
            {
                writer.Write(entry.Key);
                writer.Write(entry.Value.Priority);
                writer.Write((int)entry.Value.Filter.Mode);
                var names = entry.Value.Filter.Names.ToList();
                names.Sort(string.CompareOrdinal);
                writer.Write(names.Count);
                foreach (var name in names)
                    writer.Write(name);
            }
        }

        public static NodeConfigStore Read(BinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            try
            {
                byte[] magic = reader.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length || !magic.SequenceEqual(Magic))
                    throw new InvalidDataException("Bad node config magic.");
                int version = reader.ReadInt32();
                if (version != CurrentVersion)
                    throw new InvalidDataException("Unknown node config version: " + version);
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxNodes)
                    throw new InvalidDataException("Invalid node count: " + count);

                var store = new NodeConfigStore();
                for (int i = 0; i < count; i++)
                {
                    long key = reader.ReadInt64();
                    int priority = reader.ReadInt32();
                    if (priority < ProviderRouting.MinPriority || priority > ProviderRouting.MaxPriority)
                        throw new InvalidDataException("Invalid priority: " + priority);
                    int mode = reader.ReadInt32();
                    if (!Enum.IsDefined(typeof(FilterMode), mode))
                        throw new InvalidDataException("Invalid filter mode: " + mode);
                    int nameCount = reader.ReadInt32();
                    if (nameCount < 0 || nameCount > MaxNames)
                        throw new InvalidDataException("Invalid filter name count: " + nameCount);
                    var names = new List<string>(nameCount);
                    for (int n = 0; n < nameCount; n++)
                        names.Add(reader.ReadString());
                    if (store.Get(key) != NodeConfig.Default)
                        throw new InvalidDataException("Duplicate node key: " + key);
                    store.Set(key, new NodeConfig(priority, new ItemFilter((FilterMode)mode, names)));
                }
                store.Dirty = false;
                return store;
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Node config data is truncated.", e);
            }
        }
    }
}
