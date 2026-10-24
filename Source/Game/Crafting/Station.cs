using System;
using System.Collections.Generic;
using RefinedStorageControl.Core;
using RefinedStorageControl.Crafting;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Crafting
{
    // CRAFT-01: a real player-placed workbench, forge or campfire touching an
    // interface. Requests go into the station's own queue and the station does
    // the work with its native time, tools and fuel; RSC never emulates one.
    internal sealed class Station
    {
        // The v1 adapters. Other workstations (chemistry station, cement mixer)
        // stay out until they get their own checks.
        private static readonly HashSet<string> Supported =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "workbench", "forge", "campfire" };

        private const int FuelModule = 3;
        private const int MaterialInputModule = 4;

        private Station(Vector3i position, TileEntityWorkstation te, string blockName)
        {
            Position = position;
            Te = te;
            BlockName = blockName;
            SortKey = PositionKey.Pack(position.x, position.y, position.z);
        }

        public Vector3i Position { get; }
        public TileEntityWorkstation Te { get; }
        public string BlockName { get; }
        public long SortKey { get; }

        public string Label => Localization.Get(BlockName);

        public static bool TryResolve(World world, Vector3i pos, out Station station)
        {
            station = null;
            var blockValue = world.GetBlock(pos);
            if (blockValue.ischild)
            {
                pos = blockValue.Block.multiBlockPos.GetParentPos(pos, blockValue);
                blockValue = world.GetBlock(pos);
            }
            string name = blockValue.Block?.GetBlockName();
            if (name == null || !Supported.Contains(name))
                return false;
            if (!(world.GetTileEntity(pos) is TileEntityWorkstation te) || !te.IsPlayerPlaced)
                return false;
            station = new Station(pos, te, name);
            return true;
        }

        // Stations in the six blocks around each interface, each once, in the
        // interfaces' fixed position order.
        public static List<(Station station, Vector3i iface)> Around(World world, IEnumerable<Vector3i> interfaces)
        {
            var found = new List<(Station, Vector3i)>();
            var seen = new HashSet<Vector3i>();
            foreach (var iface in interfaces)
                foreach (var dir in Neighbors)
                    if (TryResolve(world, iface + dir, out var s) && seen.Add(s.Position))
                        found.Add((s, iface));
            return found;
        }

        private static readonly Vector3i[] Neighbors =
        {
            new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 1, 0),
            new Vector3i(0, -1, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1)
        };

        public bool UsesFuel => Te.isModuleUsed[FuelModule];
        public bool HasFuel => Te.BurnTotalTimeLeft > 0f;

        // The forge crafts from its own input and smelted materials, never from
        // the player's backpack, so terminal requests do the same (CRAFT-02).
        public bool DrawsFromStation => Te.isModuleUsed[MaterialInputModule];

        // A player has it open; their client will upload the whole station.
        public bool InUse => LockManager.Instance.IsLockedServer(Te);

        public bool HasTool(Recipe recipe)
        {
            if (recipe.craftingToolType == 0)
                return true;
            foreach (var tool in Te.Tools)
                if (tool != null && tool.itemValue.type == recipe.craftingToolType)
                    return true;
            return false;
        }

        public bool[] Occupied()
        {
            var queue = Te.Queue;
            var occupied = new bool[queue.Length];
            for (int i = 0; i < queue.Length; i++)
                occupied[i] = queue[i] != null && queue[i].Multiplier > 0 && queue[i].Recipe != null;
            return occupied;
        }

        public float QueueSeconds()
        {
            float total = 0f;
            foreach (var q in Te.Queue)
                if (q != null && q.Multiplier > 0 && q.Recipe != null)
                    total += Math.Max(0f, q.CraftingTimeLeft) + Math.Max(0f, q.OneItemCraftTime) * (q.Multiplier - 1);
            return total;
        }

        public List<Supply> InputSupplies()
        {
            var supplies = new List<Supply>();
            foreach (var stack in Te.Input)
                if (!GridEndpoint.IsEmpty(stack))
                    supplies.Add(new Supply(ItemKeys.FromItemValue(stack.itemValue), stack.itemValue.type, stack.count));
            return supplies;
        }

        public int InputCount(int type)
        {
            int n = 0;
            foreach (var stack in Te.Input)
                if (!GridEndpoint.IsEmpty(stack) && stack.itemValue.type == type)
                    n += stack.count;
            return n;
        }

        // Takes from the station's own input. Smelted-material slots keep their
        // material type at zero, as the forge does itself.
        public void TakeInput(int type, int count)
        {
            var input = Te.input;
            for (int i = 0; i < input.Length && count > 0; i++)
            {
                var stack = input[i];
                if (GridEndpoint.IsEmpty(stack) || stack.itemValue.type != type)
                    continue;
                int take = Math.Min(count, stack.count);
                stack.count -= take;
                count -= take;
                if (stack.count <= 0 && i < Te.InputSlotCount)
                    input[i] = ItemStack.Empty.Clone();
            }
            if (count > 0)
                throw new InvalidOperationException("station input short by " + count);
        }

        // Appends behind every queued job (F06). The caller checked for room.
        public void Append(RecipeQueueItem item)
        {
            var queue = Te.Queue;
            // A station nobody has saved or opened yet can still hold null
            // slots, which its queue shifting does not expect.
            for (int i = 0; i < queue.Length; i++)
                if (queue[i] == null)
                    queue[i] = new RecipeQueueItem { Recipe = null, Multiplier = 0, IsCrafting = false, StartingEntityId = -1 };
            int index = QueueSlots.AppendIndex(Occupied());
            if (index < 0)
                throw new InvalidOperationException("station queue is full");
            item.IsCrafting = index == queue.Length - 1;
            queue[index] = item;
            // Like queueing at the station by hand: a fuelled station lights up
            // if it has fuel. Refuelling stays the player's job (CRAFT-03).
            if (UsesFuel && !Te.IsBurning && HasFuel)
                Te.IsBurning = true;
            Te.SetModified();
        }

        public bool HasTag(int tag)
        {
            foreach (var q in Te.Queue)
                if (q != null && q.StartingEntityId == tag && q.Multiplier > 0 && q.Recipe != null)
                    return true;
            return false;
        }

        // Completed crafts the station recorded for this job; they leave the
        // station's list so nobody claims them twice.
        public List<CraftCompleteData> Harvest(int tag)
        {
            var found = new List<CraftCompleteData>();
            var list = Te.CraftCompleteList;
            if (list == null)
                return found;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].CrafterEntityID == tag)
                {
                    found.Insert(0, list[i]);
                    list.RemoveAt(i);
                }
            if (found.Count > 0)
                Te.SetModified();
            return found;
        }

        // A job RSC stops tracking hands its remaining crafts and records to the
        // requester, so the native station gives them the XP on their next visit.
        public void Retag(int tag, int entityId)
        {
            bool changed = false;
            foreach (var q in Te.Queue)
                if (q != null && q.StartingEntityId == tag)
                {
                    q.StartingEntityId = entityId;
                    changed = true;
                }
            if (Te.CraftCompleteList != null)
                foreach (var c in Te.CraftCompleteList)
                    if (c.CrafterEntityID == tag)
                    {
                        c.CrafterEntityID = entityId;
                        changed = true;
                    }
            if (changed)
                Te.SetModified();
        }
    }
}
