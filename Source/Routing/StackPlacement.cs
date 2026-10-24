using System;
using System.Collections.Generic;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Routing
{
    // ROUTE-04 placement planning for one source stack into a destination grid:
    // top up byte-identical stacks first (slot order, up to their max count),
    // then the first empty slot (up to the source item's own max count).
    // Non-identical item states are never merged. Pure computation; the game
    // layer applies the returned plan to real slots.
    public static class StackPlacement
    {
        public readonly struct Slot
        {
            public Slot(ItemKey? key, int count, int maxCount, bool locked = false)
            {
                Key = key;
                Count = count;
                MaxCount = maxCount;
                Locked = locked;
            }

            public ItemKey? Key { get; } // null = empty slot
            public int Count { get; }
            public int MaxCount { get; }
            public bool Locked { get; } // player slot lock: never receives items

        }

        public readonly struct Placement
        {
            public Placement(int index, int added, bool isNewStack)
            {
                Index = index;
                Added = added;
                IsNewStack = isNewStack;
            }

            public int Index { get; }
            public int Added { get; }
            public bool IsNewStack { get; }
        }

        // Appends placements to `additions` and returns the remainder that did
        // not fit. key null/empty request or missing slots => nothing moves.
        public static int Plan(ItemKey? key, int requested, int sourceMaxCount,
            IReadOnlyList<Slot> slots, List<Placement> additions)
        {
            int remaining = Math.Max(0, requested);
            if (!key.HasValue || remaining == 0 || slots == null)
                return remaining;

            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                var slot = slots[i];
                if (slot.Locked || !slot.Key.HasValue || !slot.Key.Value.Equals(key.Value))
                    continue;
                int room = slot.MaxCount - slot.Count;
                if (room <= 0)
                    continue;
                int added = Math.Min(room, remaining);
                additions?.Add(new Placement(i, added, false));
                remaining -= added;
            }

            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                if (slots[i].Locked || slots[i].Key.HasValue)
                    continue;
                int room = slots[i].MaxCount > 0
                    ? Math.Min(slots[i].MaxCount, sourceMaxCount)
                    : sourceMaxCount;
                if (room <= 0)
                    continue;
                int added = Math.Min(room, remaining);
                additions?.Add(new Placement(i, added, true));
                remaining -= added;
                break; // only the first empty slot receives a new stack
            }
            return remaining;
        }
    }
}
