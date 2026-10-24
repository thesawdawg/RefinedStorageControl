using System;
using System.Collections.Generic;
using System.IO;
using RefinedStorageControl.Access;

namespace RefinedStorageControl.Storage
{
    // Binary format v1, little-endian via BinaryWriter/BinaryReader:
    //   "RSCD" magic, int32 version, 16-byte Guid, int32 tier,
    //   string ownerId ("" = none), int32 aclEntryCount,
    //   per ACL entry { string playerId, int32 permissionFlags },
    //   int32 itemCount, per item { int32 keyLength, key bytes, int64 count }.
    // Items and ACL entries are written in sorted order so equal records
    // serialize to identical bytes regardless of insertion order.
    public static class DiskRecordSerializer
    {
        public const int CurrentVersion = 1;

        private const int MaxKeyLength = 1024 * 1024; // 1 MiB
        private static readonly byte[] Magic = { (byte)'R', (byte)'S', (byte)'C', (byte)'D' };

        public static void Write(BinaryWriter writer, DiskRecord disk)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (disk == null)
                throw new ArgumentNullException(nameof(disk));

            writer.Write(Magic);
            writer.Write(CurrentVersion);
            writer.Write(disk.Id.ToByteArray());
            writer.Write((int)disk.Tier);
            writer.Write(disk.Acl.OwnerId);

            var aclEntries = new List<KeyValuePair<string, DigitalPermission>>(disk.Acl.Entries);
            aclEntries.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
            writer.Write(aclEntries.Count);
            foreach (var entry in aclEntries)
            {
                writer.Write(entry.Key);
                writer.Write((int)entry.Value);
            }

            var items = new List<KeyValuePair<ItemKey, long>>(disk.Items);
            items.Sort((x, y) => CompareKeys(x.Key, y.Key));
            writer.Write(items.Count);
            foreach (var item in items)
            {
                byte[] keyBytes = item.Key.RawBytes;
                writer.Write(keyBytes.Length);
                writer.Write(keyBytes);
                writer.Write(item.Value);
            }
        }

        public static DiskRecord Read(BinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            try
            {
                byte[] magic = reader.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length)
                    throw Truncated();
                for (int i = 0; i < Magic.Length; i++)
                    if (magic[i] != Magic[i])
                        throw new InvalidDataException("Bad disk record magic.");

                int version = reader.ReadInt32();
                if (version != CurrentVersion)
                    throw new InvalidDataException("Unknown disk record version: " + version);

                byte[] guidBytes = reader.ReadBytes(16);
                if (guidBytes.Length != 16)
                    throw Truncated();
                var id = new Guid(guidBytes);
                if (id == Guid.Empty)
                    throw new InvalidDataException("Disk id must not be empty.");

                int tierValue = reader.ReadInt32();
                if (!Enum.IsDefined(typeof(DiskTier), tierValue))
                    throw new InvalidDataException("Undefined disk tier: " + tierValue);
                var tier = (DiskTier)tierValue;

                string ownerId = reader.ReadString();

                var acl = new DigitalAcl(ownerId);
                int aclCount = reader.ReadInt32();
                if (aclCount < 0)
                    throw new InvalidDataException("Negative ACL entry count.");
                if (aclCount > 0 && ownerId.Length == 0)
                    throw new InvalidDataException("Ownerless disk must not have ACL entries.");
                var seenPlayers = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < aclCount; i++)
                {
                    string playerId = reader.ReadString();
                    int flags = reader.ReadInt32();
                    if (playerId.Length == 0)
                        throw new InvalidDataException("ACL player id must not be empty.");
                    if (string.Equals(playerId, ownerId, StringComparison.Ordinal))
                        throw new InvalidDataException("The owner cannot appear in the allowlist.");
                    if (((DigitalPermission)flags & ~DigitalPermission.All) != 0 || flags == 0)
                        throw new InvalidDataException("ACL permission flags outside defined bits: " + flags);
                    if (!seenPlayers.Add(playerId))
                        throw new InvalidDataException("Duplicate ACL player: " + playerId);
                    acl.RestoreEntry(playerId, (DigitalPermission)flags);
                }

                var items = new Dictionary<ItemKey, long>();
                int itemCount = reader.ReadInt32();
                if (itemCount < 0)
                    throw new InvalidDataException("Negative item count.");
                long capacity = DiskCapacity.Of(tier);
                long total = 0;
                for (int i = 0; i < itemCount; i++)
                {
                    int keyLength = reader.ReadInt32();
                    if (keyLength <= 0 || keyLength > MaxKeyLength)
                        throw new InvalidDataException("Invalid item key length: " + keyLength);
                    byte[] keyBytes = reader.ReadBytes(keyLength);
                    if (keyBytes.Length != keyLength)
                        throw Truncated();
                    long count = reader.ReadInt64();
                    if (count <= 0)
                        throw new InvalidDataException("Item count must be positive: " + count);
                    if (count > capacity || total > capacity - count)
                        throw new InvalidDataException("Stored total exceeds disk capacity.");
                    var key = new ItemKey(keyBytes);
                    if (items.ContainsKey(key))
                        throw new InvalidDataException("Duplicate item key.");
                    items.Add(key, count);
                    total += count;
                }

                var disk = new DiskRecord(id, tier, acl);
                foreach (var item in items)
                    disk.Insert(item.Key, item.Value);
                return disk;
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Truncated disk record.", e);
            }
        }

        private static InvalidDataException Truncated() => new InvalidDataException("Truncated disk record.");

        private static int CompareKeys(ItemKey x, ItemKey y)
        {
            byte[] a = x.RawBytes, b = y.RawBytes;
            int shared = Math.Min(a.Length, b.Length);
            for (int i = 0; i < shared; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0)
                    return diff;
            }
            return a.Length - b.Length;
        }
    }
}
