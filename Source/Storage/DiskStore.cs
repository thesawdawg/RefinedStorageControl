using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RefinedStorageControl.Access;

namespace RefinedStorageControl.Storage
{
    // All digital storage of one world: every disk record (installed or carried
    // around as an item) and which disk sits in which cabinet slot. Disks are
    // bound to the world through WorldId (DISK-08): a disk item tagged with a
    // different world id has no record here and shows "data unavailable".
    public sealed class DiskStore
    {
        public const int CabinetSlots = 6; // DISK-07

        private readonly Dictionary<Guid, DiskRecord> _disks = new Dictionary<Guid, DiskRecord>();
        private readonly Dictionary<long, Guid?[]> _cabinets = new Dictionary<long, Guid?[]>();

        public DiskStore(Guid worldId)
        {
            if (worldId == Guid.Empty)
                throw new ArgumentException("World id must not be empty.", nameof(worldId));
            WorldId = worldId;
        }

        public Guid WorldId { get; }

        // Set on every change; the game layer clears it after a save.
        public bool Dirty { get; set; }

        public int DiskCount => _disks.Count;
        public IEnumerable<DiskRecord> Disks => _disks.Values;
        public IEnumerable<long> Cabinets => _cabinets.Keys;

        public DiskRecord Get(Guid id) => _disks.TryGetValue(id, out var disk) ? disk : null;

        // ACCESS-04: a blank disk gets its record, and its owner, on first insert.
        public DiskRecord Create(DiskTier tier, string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
                throw new ArgumentException("A new disk needs an owner.", nameof(ownerId));
            var disk = new DiskRecord(Guid.NewGuid(), tier, new DigitalAcl(ownerId));
            _disks.Add(disk.Id, disk);
            Dirty = true;
            return disk;
        }

        // DISK-04/06: a destroyed disk loses its record for good. It must already
        // be out of every cabinet, so a stale slot can never bring it back.
        public void Destroy(Guid id)
        {
            if (FindCabinet(id).HasValue)
                throw new InvalidOperationException("Remove the disk from its cabinet before destroying it.");
            if (_disks.Remove(id))
                Dirty = true;
        }

        public Guid?[] Slots(long cabinet) =>
            _cabinets.TryGetValue(cabinet, out var slots) ? (Guid?[])slots.Clone() : new Guid?[CabinetSlots];

        public bool IsEmpty(long cabinet) => !_cabinets.TryGetValue(cabinet, out var slots) || slots.All(s => !s.HasValue);

        public (long cabinet, int slot)? FindCabinet(Guid disk)
        {
            foreach (var pair in _cabinets)
                for (int i = 0; i < CabinetSlots; i++)
                    if (pair.Value[i] == disk)
                        return (pair.Key, i);
            return null;
        }

        // A disk can sit in only one slot anywhere, which blocks duplicating a
        // disk through a stale item copy.
        public void Install(long cabinet, int slot, Guid disk)
        {
            CheckSlot(slot);
            if (!_disks.ContainsKey(disk))
                throw new InvalidOperationException("Unknown disk.");
            if (FindCabinet(disk).HasValue)
                throw new InvalidOperationException("The disk is already installed.");
            if (!_cabinets.TryGetValue(cabinet, out var slots))
                _cabinets[cabinet] = slots = new Guid?[CabinetSlots];
            if (slots[slot].HasValue)
                throw new InvalidOperationException("The slot is occupied.");
            slots[slot] = disk;
            Dirty = true;
        }

        public Guid? Uninstall(long cabinet, int slot)
        {
            CheckSlot(slot);
            if (!_cabinets.TryGetValue(cabinet, out var slots) || !slots[slot].HasValue)
                return null;
            var disk = slots[slot];
            slots[slot] = null;
            if (slots.All(s => !s.HasValue))
                _cabinets.Remove(cabinet);
            Dirty = true;
            return disk;
        }

        // Empties a cabinet that is going away; the caller decides each disk's fate.
        public List<Guid> RemoveCabinet(long cabinet)
        {
            var removed = new List<Guid>();
            if (_cabinets.TryGetValue(cabinet, out var slots))
            {
                removed.AddRange(slots.Where(s => s.HasValue).Select(s => s.Value));
                _cabinets.Remove(cabinet);
                Dirty = true;
            }
            return removed;
        }

        internal void Restore(DiskRecord disk) => _disks.Add(disk.Id, disk);

        internal void RestoreCabinet(long cabinet, Guid?[] slots) => _cabinets[cabinet] = slots;

        private static void CheckSlot(int slot)
        {
            if (slot < 0 || slot >= CabinetSlots)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Cabinet slot must be 0-5.");
        }
    }

    // Binary format v1, little-endian via BinaryWriter/BinaryReader:
    //   "RSCS" magic, int32 version, 16-byte world Guid,
    //   int32 diskCount, per disk { DiskRecordSerializer record },
    //   int32 cabinetCount, per cabinet { int64 key, 6 x { bool present, [16-byte Guid] } }.
    // Disks are written in id order and cabinets in key order, so equal stores
    // serialize to identical bytes.
    public static class DiskStoreSerializer
    {
        public const int CurrentVersion = 1;

        private const int MaxDisks = 1 << 20;
        private static readonly byte[] Magic = { (byte)'R', (byte)'S', (byte)'C', (byte)'S' };

        public static void Write(BinaryWriter writer, DiskStore store)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            writer.Write(Magic);
            writer.Write(CurrentVersion);
            writer.Write(store.WorldId.ToByteArray());
            var disks = store.Disks.OrderBy(d => d.Id).ToList();
            writer.Write(disks.Count);
            foreach (var disk in disks)
                DiskRecordSerializer.Write(writer, disk);
            var cabinets = store.Cabinets.OrderBy(c => c).ToList();
            writer.Write(cabinets.Count);
            foreach (long cabinet in cabinets)
            {
                writer.Write(cabinet);
                foreach (var slot in store.Slots(cabinet))
                {
                    writer.Write(slot.HasValue);
                    if (slot.HasValue)
                        writer.Write(slot.Value.ToByteArray());
                }
            }
        }

        public static DiskStore Read(BinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            try
            {
                byte[] magic = reader.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length || !magic.SequenceEqual(Magic))
                    throw new InvalidDataException("Bad disk store magic.");
                int version = reader.ReadInt32();
                if (version != CurrentVersion)
                    throw new InvalidDataException("Unknown disk store version: " + version);
                var worldId = ReadGuid(reader);
                if (worldId == Guid.Empty)
                    throw new InvalidDataException("World id must not be empty.");
                var store = new DiskStore(worldId);

                int diskCount = reader.ReadInt32();
                if (diskCount < 0 || diskCount > MaxDisks)
                    throw new InvalidDataException("Invalid disk count: " + diskCount);
                for (int i = 0; i < diskCount; i++)
                {
                    var disk = DiskRecordSerializer.Read(reader);
                    if (store.Get(disk.Id) != null)
                        throw new InvalidDataException("Duplicate disk id: " + disk.Id);
                    store.Restore(disk);
                }

                int cabinetCount = reader.ReadInt32();
                if (cabinetCount < 0 || cabinetCount > MaxDisks)
                    throw new InvalidDataException("Invalid cabinet count: " + cabinetCount);
                var seen = new HashSet<Guid>();
                for (int i = 0; i < cabinetCount; i++)
                {
                    long key = reader.ReadInt64();
                    var slots = new Guid?[DiskStore.CabinetSlots];
                    for (int s = 0; s < slots.Length; s++)
                    {
                        if (!reader.ReadBoolean())
                            continue;
                        var id = ReadGuid(reader);
                        if (store.Get(id) == null)
                            throw new InvalidDataException("Cabinet references an unknown disk: " + id);
                        if (!seen.Add(id))
                            throw new InvalidDataException("Disk installed twice: " + id);
                        slots[s] = id;
                    }
                    store.RestoreCabinet(key, slots);
                }
                store.Dirty = false;
                return store;
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Disk store data is truncated.", e);
            }
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16)
                throw new EndOfStreamException();
            return new Guid(bytes);
        }
    }
}
