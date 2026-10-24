using System;
using System.Collections.Generic;
using RefinedStorageControl.Access;

namespace RefinedStorageControl.Storage
{
    // Sparse item accounting for a single disk: one entry per distinct item
    // state, entries are removed when their count reaches zero.
    public sealed class DiskRecord
    {
        private readonly Dictionary<ItemKey, long> _items = new Dictionary<ItemKey, long>();
        private long _used;

        public DiskRecord(Guid id, DiskTier tier, DigitalAcl acl)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Disk id must not be empty.", nameof(id));
            Id = id;
            Tier = tier;
            Capacity = DiskCapacity.Of(tier);
            Acl = acl ?? throw new ArgumentNullException(nameof(acl));
        }

        public Guid Id { get; }
        public DiskTier Tier { get; }
        public long Capacity { get; }
        public long Used => _used;
        public long Free => Capacity - _used;
        public DigitalAcl Acl { get; }
        public IReadOnlyDictionary<ItemKey, long> Items => _items;

        public long Count(ItemKey key) => _items.TryGetValue(key, out var count) ? count : 0;

        public long Insert(ItemKey key, long requested)
        {
            if (requested <= 0)
                throw new ArgumentOutOfRangeException(nameof(requested), requested, "Insert amount must be positive.");
            long accepted = Math.Min(requested, Free);
            if (accepted > 0)
            {
                _items[key] = Count(key) + accepted;
                _used += accepted;
            }
            return accepted;
        }

        public long Extract(ItemKey key, long requested)
        {
            if (requested <= 0)
                throw new ArgumentOutOfRangeException(nameof(requested), requested, "Extract amount must be positive.");
            long released = Math.Min(requested, Count(key));
            if (released > 0)
            {
                long remaining = _items[key] - released;
                if (remaining == 0)
                    _items.Remove(key);
                else
                    _items[key] = remaining;
                _used -= released;
            }
            return released;
        }
    }
}
