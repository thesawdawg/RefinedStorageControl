using System;
using System.Collections.Generic;
using RefinedStorageControl.Access;
using RefinedStorageControl.Core;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Providers
{
    // One place the network can store items: a real container next to an
    // interface (PHY-01) or an installed disk (DISK-01..03). Built fresh for each
    // request; a deposit plan is kept on the instance between PlanDeposit and
    // CommitDeposit in the same main-thread step.
    internal abstract class StorageProvider
    {
        public abstract long SortKey { get; }

        // The interface or cabinet whose priority and filter apply.
        public abstract NodeId? ConfigNode { get; }

        // The grid behind it, so a transfer never routes back into its own source.
        public abstract ItemStackGrid Grid { get; }

        public abstract bool CanView(PlatformUserIdentifierAbs user);
        public abstract bool CanDeposit(PlatformUserIdentifierAbs user);
        public abstract bool CanWithdraw(PlatformUserIdentifierAbs user);
        public abstract bool InUse { get; }
        public abstract bool Holds(ItemKey key);

        // How much of `count` it would accept; remembered for CommitDeposit.
        public abstract int PlanDeposit(ItemKey key, int count, int maxCount);
        public abstract void CommitDeposit(ItemValue item);

        // Places holding the exact item: (handle, how many). One source = one transfer.
        public abstract IEnumerable<(int handle, int available)> Sources(ItemKey key);
        public abstract ItemValue SampleAt(int handle, ItemKey key);
        public abstract void Take(int handle, ItemKey key, int count);

        public abstract void MarkModified();
        public abstract void AddEntries(Dictionary<ItemKey, TransferService.Entry> byKey);
    }

    internal sealed class ContainerStorage : StorageProvider
    {
        private readonly ContainerProvider _container;
        private readonly NodeId _interface;
        private List<StackPlacement.Placement> _plan;

        public ContainerStorage(ContainerProvider container, NodeId iface)
        {
            _container = container;
            _interface = iface;
        }

        public override long SortKey => _container.SortKey;
        public override NodeId? ConfigNode => _interface;
        public override ItemStackGrid Grid => _container.Grid;

        // Containers follow native access for everything (ACCESS-01).
        public override bool CanView(PlatformUserIdentifierAbs user) => _container.CanAccess(user);
        public override bool CanDeposit(PlatformUserIdentifierAbs user) => _container.CanAccess(user);
        public override bool CanWithdraw(PlatformUserIdentifierAbs user) => _container.CanAccess(user);
        public override bool InUse => _container.InUse;
        public override bool Holds(ItemKey key) => _container.Holds(key);

        public override int PlanDeposit(ItemKey key, int count, int maxCount)
        {
            // A stack can exceed one slot's room after top-ups, so keep planning
            // on this container until it stops accepting.
            var slots = _container.PlacementSlots();
            _plan = new List<StackPlacement.Placement>();
            int remaining = count;
            while (remaining > 0)
            {
                int before = remaining;
                var step = new List<StackPlacement.Placement>();
                remaining = StackPlacement.Plan(key, remaining, maxCount, slots, step);
                if (remaining == before)
                    break;
                foreach (var p in step)
                {
                    _plan.Add(p);
                    var s = slots[p.Index];
                    slots[p.Index] = new StackPlacement.Slot(key, s.Count + p.Added, maxCount, s.Locked);
                }
            }
            return count - remaining;
        }

        public override void CommitDeposit(ItemValue item)
        {
            if (_plan != null && _plan.Count > 0)
                _container.Apply(_plan, item);
            _plan = null;
        }

        public override IEnumerable<(int handle, int available)> Sources(ItemKey key)
        {
            for (int s = 0; s < _container.Grid.Length; s++)
            {
                if (_container.SlotLocked(s))
                    continue;
                var k = _container.KeyAt(s);
                if (k.HasValue && k.Value.Equals(key))
                    yield return (s, _container.Grid[s].count);
            }
        }

        public override ItemValue SampleAt(int handle, ItemKey key) => _container.Grid[handle].itemValue.Clone();
        public override void Take(int handle, ItemKey key, int count) => _container.Take(handle, count);
        public override void MarkModified() => _container.MarkModified();

        public override void AddEntries(Dictionary<ItemKey, TransferService.Entry> byKey)
        {
            var seen = new HashSet<ItemKey>();
            for (int i = 0; i < _container.Grid.Length; i++)
            {
                var stack = _container.Grid[i];
                if (GridEndpoint.IsEmpty(stack))
                    continue;
                var key = ItemKeys.FromItemValue(stack.itemValue);
                if (!byKey.TryGetValue(key, out var e))
                    byKey[key] = e = new TransferService.Entry { Key = key, Sample = stack.itemValue.Clone() };
                e.Count += stack.count;
                if (seen.Add(key))
                    e.Providers++;
            }
        }
    }

    // An installed disk on a powered advanced network. Access follows the disk's
    // owner + allowlist (ACCESS-03): seeing and taking need Withdraw, storing
    // needs Deposit. Capacity counts individual items (DISK-01), not stacks.
    internal sealed class DiskStorage : StorageProvider
    {
        private readonly DiskStore _store;
        private readonly DiskRecord _disk;
        private readonly NodeId _cabinet;
        private readonly long _sortKey;
        private (ItemKey key, int count)? _plan;

        public DiskStorage(DiskStore store, DiskRecord disk, NodeId cabinet, int slot)
        {
            _store = store;
            _disk = disk;
            _cabinet = cabinet;
            _sortKey = cabinet.Value * DiskStore.CabinetSlots + slot; // fixed order: cabinet, then slot
        }

        public override long SortKey => _sortKey;
        public override NodeId? ConfigNode => _cabinet;
        public override ItemStackGrid Grid => null;

        public override bool CanView(PlatformUserIdentifierAbs user) => Has(user, DigitalPermission.Withdraw);
        public override bool CanDeposit(PlatformUserIdentifierAbs user) => Has(user, DigitalPermission.Deposit);
        public override bool CanWithdraw(PlatformUserIdentifierAbs user) => Has(user, DigitalPermission.Withdraw);
        public override bool InUse => false;
        public override bool Holds(ItemKey key) => _disk.Count(key) > 0;

        public override int PlanDeposit(ItemKey key, int count, int maxCount)
        {
            int accepted = (int)Math.Min(count, _disk.Free);
            _plan = accepted > 0 ? (key, accepted) : ((ItemKey, int)?)null;
            return accepted;
        }

        public override void CommitDeposit(ItemValue item)
        {
            if (_plan.HasValue)
                _disk.Insert(_plan.Value.key, _plan.Value.count);
            _plan = null;
        }

        public override IEnumerable<(int handle, int available)> Sources(ItemKey key)
        {
            long count = _disk.Count(key);
            if (count > 0)
                yield return (0, (int)Math.Min(count, int.MaxValue));
        }

        public override ItemValue SampleAt(int handle, ItemKey key) => ItemKeys.ToItemValue(key);
        public override void Take(int handle, ItemKey key, int count) => _disk.Extract(key, count);
        public override void MarkModified() => _store.Dirty = true;

        public override void AddEntries(Dictionary<ItemKey, TransferService.Entry> byKey)
        {
            foreach (var pair in _disk.Items)
            {
                if (!byKey.TryGetValue(pair.Key, out var e))
                {
                    var sample = ItemKeys.ToItemValue(pair.Key);
                    if (sample.ItemClass == null)
                        continue; // item type no longer exists (mod removed); keep the record, hide the entry
                    byKey[pair.Key] = e = new TransferService.Entry { Key = pair.Key, Sample = sample };
                }
                e.Count = (int)Math.Min((long)e.Count + pair.Value, int.MaxValue);
                e.Providers++;
            }
        }

        private bool Has(PlatformUserIdentifierAbs user, DigitalPermission perm) =>
            user != null && _disk.Acl.Has(user.CombinedString, perm);
    }
}
