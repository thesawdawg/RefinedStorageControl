using System.Collections.Generic;
using System.IO;
using System.Linq;
using RefinedStorageControl.Access;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Persistence;
using RefinedStorageControl.Game.Topology;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Providers
{
    // What one player sees in a drive cabinet window. Built on the server; sent
    // to co-op clients as-is.
    internal sealed class CabinetView
    {
        public sealed class Entry
        {
            public string Id;
            public string Name;
            public DigitalPermission Perms;
        }

        public sealed class Slot
        {
            public bool Present;
            public DiskTier Tier;
            public long Used;
            public long Capacity;
            public string OwnerId = "";
            public string OwnerName = "";
            public bool IsOwner;
            public DigitalPermission MyPerms;
            public List<Entry> Entries = new List<Entry>();
        }

        public string Error;
        public Slot[] Slots = Enumerable.Range(0, DiskStore.CabinetSlots).Select(_ => new Slot()).ToArray();
        public List<Entry> Candidates = new List<Entry>(); // known players who could be added

        public void Write(BinaryWriter w)
        {
            w.Write(Error != null);
            if (Error != null)
                w.Write(Error);
            foreach (var s in Slots)
            {
                w.Write(s.Present);
                if (!s.Present)
                    continue;
                w.Write((int)s.Tier);
                w.Write(s.Used);
                w.Write(s.Capacity);
                w.Write(s.OwnerId ?? "");
                w.Write(s.OwnerName ?? "");
                w.Write(s.IsOwner);
                w.Write((int)s.MyPerms);
                WriteEntries(w, s.Entries);
            }
            WriteEntries(w, Candidates);
        }

        public static CabinetView Read(BinaryReader r)
        {
            var view = new CabinetView();
            if (r.ReadBoolean())
                view.Error = r.ReadString();
            foreach (var s in view.Slots)
            {
                s.Present = r.ReadBoolean();
                if (!s.Present)
                    continue;
                s.Tier = (DiskTier)r.ReadInt32();
                s.Used = r.ReadInt64();
                s.Capacity = r.ReadInt64();
                s.OwnerId = r.ReadString();
                s.OwnerName = r.ReadString();
                s.IsOwner = r.ReadBoolean();
                s.MyPerms = (DigitalPermission)r.ReadInt32();
                s.Entries = ReadEntries(r);
            }
            view.Candidates = ReadEntries(r);
            return view;
        }

        private static void WriteEntries(BinaryWriter w, List<Entry> entries)
        {
            w.Write(entries.Count);
            foreach (var e in entries)
            {
                w.Write(e.Id);
                w.Write(e.Name ?? "");
                w.Write((int)e.Perms);
            }
        }

        private static List<Entry> ReadEntries(BinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<Entry>();
            for (int i = 0; i < n && i < 1024; i++)
                list.Add(new Entry { Id = r.ReadString(), Name = r.ReadString(), Perms = (DigitalPermission)r.ReadInt32() });
            return list;
        }
    }

    // Server-side drive cabinet rules (ACCESS-04, DISK-02/07/08). Anyone in reach
    // may insert a disk into a free slot; a blank disk becomes the inserter's.
    // Taking a disk out, and editing its allowlist, needs the owner or Configure;
    // transferring ownership needs the owner. A cabinet works without power or a
    // network: disks are only usable for storage on a powered advanced network.
    internal static class CabinetService
    {
        public static string Id(PlatformUserIdentifierAbs user) => user?.CombinedString;

        public static string NameOf(string id)
        {
            var players = GameManager.Instance.persistentPlayers?.Players;
            if (players != null && !string.IsNullOrEmpty(id))
                foreach (var pair in players)
                    if (pair.Key?.CombinedString == id || pair.Value?.PrimaryId?.CombinedString == id)
                        return pair.Value.PlayerName?.DisplayName ?? id;
            return string.IsNullOrEmpty(id) ? "nobody" : "unknown player";
        }

        public static string InsertDisk(PlatformUserIdentifierAbs user, Vector3i pos, int preferredSlot, ItemValue item, out int slot)
        {
            slot = -1;
            if (!TryCabinet(pos, out long key, out var store, out string reason))
                return reason;
            switch (DiskItems.Describe(item, store, out var record))
            {
                case DiskState.NotADisk: return "That is not a storage disk";
                case DiskState.OtherWorld: return "Data unavailable: this disk belongs to another world";
                case DiskState.Missing: return "This disk's data was destroyed";
            }
            var slots = store.Slots(key);
            slot = preferredSlot >= 0 && preferredSlot < slots.Length && !slots[preferredSlot].HasValue
                ? preferredSlot
                : System.Array.FindIndex(slots, s => !s.HasValue);
            if (slot < 0)
                return "The cabinet is full";
            if (record == null)
            {
                DiskItems.TryGetTier(item, out var tier);
                record = store.Create(tier, Id(user)); // ACCESS-04: first insert of a blank disk sets the owner
            }
            else if (store.FindCabinet(record.Id).HasValue)
                return "This disk is already installed somewhere else";
            store.Install(key, slot, record.Id);
            RscLog.Out("disk " + record.Id.ToString("N") + " (" + record.Tier + ") installed at " + pos + "[" + slot + "] by "
                + NameOf(Id(user)));
            return null;
        }

        public static string RemoveDisk(PlatformUserIdentifierAbs user, Vector3i pos, int slot, out ItemValue diskItem)
        {
            diskItem = null;
            if (!TryDisk(pos, slot, out long key, out var store, out var record, out string reason))
                return reason;
            if (!record.Acl.Has(Id(user), DigitalPermission.Configure))
                return "Only the owner or players with Configure can take this disk out";
            store.Uninstall(key, slot);
            diskItem = DiskItems.Create(record.Tier, record.Id, store.WorldId);
            RscLog.Out("disk " + record.Id.ToString("N") + " removed from " + pos + "[" + slot + "] by " + NameOf(Id(user)));
            return null;
        }

        public static string SetPermissions(PlatformUserIdentifierAbs user, Vector3i pos, int slot, string targetId, DigitalPermission perms)
        {
            if (!TryDisk(pos, slot, out _, out var store, out var record, out string reason))
                return reason;
            if (!record.Acl.Has(Id(user), DigitalPermission.Configure))
                return "Only the owner or players with Configure can change who has access";
            if (string.IsNullOrEmpty(targetId) || targetId == record.Acl.OwnerId)
                return "Pick another player";
            record.Acl.Set(Id(user), targetId, perms & DigitalPermission.All);
            store.Dirty = true;
            return null;
        }

        public static string TransferOwnership(PlatformUserIdentifierAbs user, Vector3i pos, int slot, string targetId)
        {
            if (!TryDisk(pos, slot, out _, out var store, out var record, out string reason))
                return reason;
            if (Id(user) != record.Acl.OwnerId)
                return "Only the owner can give the disk away";
            if (string.IsNullOrEmpty(targetId) || !record.Acl.Entries.ContainsKey(targetId))
                return "Ownership can only go to a player on the list";
            record.Acl.TransferOwnership(Id(user), targetId);
            store.Dirty = true;
            RscLog.Out("disk " + record.Id.ToString("N") + " ownership " + NameOf(Id(user)) + " -> " + NameOf(targetId));
            return null;
        }

        public static CabinetView BuildView(PlatformUserIdentifierAbs user, Vector3i pos)
        {
            var view = new CabinetView();
            if (!TryCabinet(pos, out long key, out var store, out string reason))
            {
                view.Error = reason;
                return view;
            }
            string me = Id(user);
            var slots = store.Slots(key);
            for (int i = 0; i < slots.Length; i++)
            {
                var record = slots[i].HasValue ? store.Get(slots[i].Value) : null;
                if (record == null)
                    continue;
                var s = view.Slots[i];
                s.Present = true;
                s.Tier = record.Tier;
                s.Used = record.Used;
                s.Capacity = record.Capacity;
                s.OwnerId = record.Acl.OwnerId;
                s.OwnerName = NameOf(record.Acl.OwnerId);
                s.IsOwner = me == record.Acl.OwnerId;
                s.MyPerms = s.IsOwner ? DigitalPermission.All
                    : record.Acl.Entries.TryGetValue(me ?? "", out var mine) ? mine : DigitalPermission.None;
                s.Entries = record.Acl.Entries
                    .Select(e => new CabinetView.Entry { Id = e.Key, Name = NameOf(e.Key), Perms = e.Value })
                    .OrderBy(e => e.Name, System.StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            var players = GameManager.Instance.persistentPlayers?.Players;
            if (players != null)
                foreach (var pair in players)
                {
                    string id = pair.Value?.PrimaryId?.CombinedString ?? pair.Key?.CombinedString;
                    if (!string.IsNullOrEmpty(id))
                        view.Candidates.Add(new CabinetView.Entry { Id = id, Name = pair.Value?.PlayerName?.DisplayName ?? id });
                }
            view.Candidates.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.CurrentCultureIgnoreCase));
            return view;
        }

        // ACCESS-04 host override: reassign a disk, e.g. one whose owner left for good.
        public static string ForceOwner(string diskIdPrefix, string newOwnerId)
        {
            var store = DiskFile.Store;
            if (store == null)
                return "no disk store";
            var matches = store.Disks.Where(d => d.Id.ToString("N").StartsWith(diskIdPrefix, System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1)
                return matches.Count == 0 ? "no disk with id " + diskIdPrefix : "id prefix matches several disks";
            matches[0].Acl.ForceOwner(newOwnerId);
            store.Dirty = true;
            return null;
        }

        private static bool TryCabinet(Vector3i pos, out long key, out DiskStore store, out string reason)
        {
            key = PositionKey.Pack(pos.x, pos.y, pos.z);
            store = DiskFile.Store;
            var world = GameManager.Instance.World;
            if (store == null || world == null)
                reason = "Disk storage is not available";
            else if (!RscBlocks.TryGetRole(world.GetBlock(pos).Block, out var kind, out _) || kind != NodeKind.DriveCabinet)
                reason = "The cabinet is gone";
            else
            {
                reason = null;
                return true;
            }
            return false;
        }

        private static bool TryDisk(Vector3i pos, int slot, out long key, out DiskStore store, out DiskRecord record, out string reason)
        {
            record = null;
            if (!TryCabinet(pos, out key, out store, out reason))
                return false;
            if (slot < 0 || slot >= DiskStore.CabinetSlots)
            {
                reason = "No such slot";
                return false;
            }
            var id = store.Slots(key)[slot];
            record = id.HasValue ? store.Get(id.Value) : null;
            if (record == null)
            {
                reason = "That slot is empty";
                return false;
            }
            return true;
        }
    }
}
