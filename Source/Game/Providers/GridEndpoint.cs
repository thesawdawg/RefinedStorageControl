using System;
using System.Collections.Generic;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Providers
{
    // One side of a transfer: a real ItemStackGrid plus the rules for touching
    // it. Network containers and the player's backpack both derive from this.
    internal abstract class GridEndpoint
    {
        public abstract ItemStackGrid Grid { get; }
        public abstract string Label { get; }
        public abstract bool CanAccess(PlatformUserIdentifierAbs user);
        public abstract bool InUse { get; }
        public abstract void MarkModified();

        public bool SlotLocked(int index)
        {
            var locks = Grid.SlotLocks;
            return locks != null && index < locks.Length && locks[index];
        }

        public static bool IsEmpty(ItemStack stack) => stack == null || stack.IsEmpty();

        public ItemKey? KeyAt(int index)
        {
            var stack = Grid[index];
            return IsEmpty(stack) ? (ItemKey?)null : ItemKeys.FromItemValue(stack.itemValue);
        }

        public bool Holds(ItemKey key)
        {
            for (int i = 0; i < Grid.Length; i++)
            {
                var k = KeyAt(i);
                if (k.HasValue && k.Value.Equals(key))
                    return true;
            }
            return false;
        }

        public List<StackPlacement.Slot> PlacementSlots()
        {
            var slots = new List<StackPlacement.Slot>(Grid.Length);
            for (int i = 0; i < Grid.Length; i++)
            {
                var s = Grid[i];
                bool locked = SlotLocked(i);
                slots.Add(IsEmpty(s)
                    ? new StackPlacement.Slot(null, 0, 0, locked)
                    : new StackPlacement.Slot(ItemKeys.FromItemValue(s.itemValue), s.count,
                        s.itemValue.ItemClass?.MaxCount ?? s.count, locked));
            }
            return slots;
        }

        // Applies a plan computed from PlacementSlots() in the same main-thread step.
        public void Apply(IEnumerable<StackPlacement.Placement> placements, ItemValue item)
        {
            foreach (var p in placements)
            {
                if (p.IsNewStack)
                    Grid[p.Index] = new ItemStack(item.Clone(), p.Added);
                else
                    Grid[p.Index].count += p.Added;
            }
        }

        // How many of this exact item fit, filling matching stacks and any number
        // of empty slots.
        public int RoomFor(ItemKey key, int maxCount)
        {
            int room = 0;
            for (int i = 0; i < Grid.Length; i++)
            {
                if (SlotLocked(i))
                    continue;
                var k = KeyAt(i);
                if (!k.HasValue)
                    room += maxCount;
                else if (k.Value.Equals(key))
                    room += Math.Max(0, maxCount - Grid[i].count);
            }
            return room;
        }

        // Puts up to `count` of the item into this grid (matching stacks first,
        // then empty slots) and returns how many did not fit.
        public int Insert(ItemValue item, int count)
        {
            var key = ItemKeys.FromItemValue(item);
            int maxCount = item.ItemClass?.MaxCount ?? count;
            int remaining = count;
            while (remaining > 0)
            {
                var adds = new List<StackPlacement.Placement>();
                int left = StackPlacement.Plan(key, remaining, maxCount, PlacementSlots(), adds);
                if (left == remaining)
                    break;
                Apply(adds, item);
                remaining = left;
            }
            if (remaining != count)
                MarkModified();
            return remaining;
        }

        public void Take(int index, int count)
        {
            var stack = Grid[index];
            stack.count -= count;
            if (stack.count <= 0)
                Grid[index] = ItemStack.Empty; // SetItem clears the slot in place
        }
    }

    // The host's own backpack. A remote client's bag is owned by that client, so
    // the server never edits it directly; client support needs network packages.
    internal sealed class BagEndpoint : GridEndpoint
    {
        private readonly EntityPlayerLocal _player;

        public BagEndpoint(EntityPlayerLocal player) => _player = player;

        public override ItemStackGrid Grid => _player.bag.ItemGrid;
        public override string Label => "backpack";
        public override bool CanAccess(PlatformUserIdentifierAbs user) => true;
        public override bool InUse => false;
        public override void MarkModified() => _player.bag.onBackpackChanged();
    }
}
