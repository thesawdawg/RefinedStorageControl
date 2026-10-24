using System.Collections.Generic;
using RefinedStorageControl.Core;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Providers
{
    // PHY-01: a view over a real vanilla container's own ItemStackGrid. Nothing
    // is copied; every change is written back to the container and broadcast
    // with SetModified. Only player-placed containers are eligible (PHY-02), so
    // the network never touches unopened or respawning loot.
    internal sealed class ContainerProvider
    {
        private ContainerProvider(Vector3i position, TileEntityComposite te, TEFeatureStorage storage)
        {
            Position = position;
            Te = te;
            Storage = storage;
            SortKey = PositionKey.Pack(position.x, position.y, position.z);
        }

        public Vector3i Position { get; }
        public TileEntityComposite Te { get; }
        public TEFeatureStorage Storage { get; }
        public long SortKey { get; }
        public ItemStackGrid Grid => Storage.itemGrid;

        // Resolves child blocks of multi-block containers to their parent.
        public static bool TryResolve(World world, Vector3i pos, out ContainerProvider provider, out string reason)
        {
            provider = null;
            var blockValue = world.GetBlock(pos);
            if (blockValue.ischild)
                pos = blockValue.Block.multiBlockPos.GetParentPos(pos, blockValue);
            if (!(world.GetTileEntity(pos) is TileEntityComposite te))
            {
                reason = "no container at " + pos;
                return false;
            }
            var storage = te.GetFeature<TEFeatureStorage>();
            if (storage == null)
            {
                reason = "no storage at " + pos;
                return false;
            }
            if (!te.PlayerPlaced)
            {
                reason = "not a player-placed container at " + pos;
                return false;
            }
            provider = new ContainerProvider(pos, te, storage);
            reason = null;
            return true;
        }

        public bool CanAccess(PlatformUserIdentifierAbs user) => NativeAccess.CanAccessStorage(Te, user);

        // A player has the container open; their client will upload its whole
        // state, so host edits now could be overwritten or duplicate items (A01).
        public bool InUse => LockManager.Instance.IsLockedServer(Storage);

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

        public void Take(int index, int count)
        {
            var stack = Grid[index];
            stack.count -= count;
            if (stack.count <= 0)
                Grid[index] = ItemStack.Empty; // SetItem clears the slot in place
        }

        public void MarkModified() => Storage.SetModified();
    }
}
