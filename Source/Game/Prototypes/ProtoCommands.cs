using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Routing;
using UnityEngine;

namespace RefinedStorageControl.Game.Prototypes
{
    // P0 in-game prototypes dispatched from `rsc proto ...`. Host/server only;
    // every result line is prefixed "[RSC proto]" and mirrored to the log.
    internal static class ProtoCommands
    {
        private const string DiskMetadataKey = "rsc.disk";

        private static readonly Dictionary<int, ChunkManager.ChunkObserver> Observers =
            new Dictionary<int, ChunkManager.ChunkObserver>();

        public static void Dispatch(List<string> args, CommandSenderInfo senderInfo)
        {
            if (!ConnectionManager.Instance.IsServer)
            {
                Say("rejected: proto commands are host/server only");
                return;
            }
            var player = ResolvePlayer(senderInfo);
            if (player == null)
            {
                Say("rejected: could not resolve acting player");
                return;
            }
            if (args.Count == 0)
            {
                Say("usage: rsc proto <look|power|container|move|disk|station|queue|net|observe|unobserve|observers> ...");
                return;
            }
            var rest = args.GetRange(1, args.Count - 1);
            switch (args[0].ToLowerInvariant())
            {
                case "power": Power(rest); break;
                case "container": Container(rest, senderInfo); break;
                case "move": Move(rest, senderInfo); break;
                case "disk": Disk(rest, senderInfo); break;
                case "station": Station(rest); break;
                case "queue": QueueCmd(rest, player); break;
                case "net": Net(rest); break;
                case "observe": Observe(rest); break;
                case "unobserve": Unobserve(rest); break;
                case "observers": ListObservers(); break;
                case "look": Look(player); break;
                default: Say("unknown proto subcommand '" + args[0] + "'"); break;
            }
        }

        public static void OnWorldShuttingDown()
        {
            var chunkManager = GameManager.Instance.World?.m_ChunkManager;
            if (chunkManager != null)
                foreach (var observer in Observers.Values)
                    chunkManager.RemoveChunkObserver(observer);
            Observers.Clear();
        }

        private static void Say(string message)
        {
            SdtdConsole.Instance.Output("[RSC proto] " + message);
            RscLog.Out("proto " + message);
        }

        private static EntityPlayer ResolvePlayer(CommandSenderInfo senderInfo)
        {
            var world = GameManager.Instance.World;
            if (world == null)
                return null;
            if (senderInfo.IsLocalGame)
                return world.GetPrimaryPlayer();
            if (senderInfo.RemoteClientInfo != null)
                return world.GetEntity(senderInfo.RemoteClientInfo.entityId) as EntityPlayer;
            return null;
        }

        private static PlatformUserIdentifierAbs ResolveUserId(CommandSenderInfo senderInfo)
        {
            if (senderInfo.IsLocalGame)
                return Platform.PlatformManager.InternalLocalUserIdentifier;
            return senderInfo.RemoteClientInfo?.InternalId;
        }

        private static bool TryParseVec(List<string> args, int start, out Vector3i pos)
        {
            pos = default;
            if (args.Count < start + 3
                || !int.TryParse(args[start], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(args[start + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
                || !int.TryParse(args[start + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
                return false;
            pos = new Vector3i(x, y, z);
            return true;
        }

        private static bool TryGetStorage(Vector3i pos, out TileEntityComposite te, out TEFeatureStorage storage, out string reason)
        {
            te = GameManager.Instance.World.GetTileEntity(pos) as TileEntityComposite;
            if (te == null)
            {
                storage = null;
                reason = "no composite tile entity at " + pos;
                return false;
            }
            storage = te.GetFeature<TEFeatureStorage>();
            if (storage == null)
            {
                reason = "no storage feature at " + pos;
                return false;
            }
            reason = null;
            return true;
        }

        private static void Power(List<string> args)
        {
            if (args.Count != 3 || !TryParseVec(args, 0, out var pos))
            {
                Say("usage: rsc proto power <x> <y> <z>");
                return;
            }
            var item = PowerManager.Instance.GetPowerItemByWorldPos(pos);
            if (item == null)
            {
                Say("no power item at " + pos);
                return;
            }
            var world = GameManager.Instance.World;
            string blockName = world.GetBlock(pos).Block?.GetBlockName() ?? "?";
            string parent = item.Parent != null ? item.Parent.Position.ToString() : "none";
            var root = item.GetRoot();
            string children = string.Join(",", item.Children.ConvertAll(c => c.Position.ToString()));
            int childCount = item.TileEntity != null ? item.TileEntity.ChildCount : item.Children.Count;
            Say("power '" + blockName + "' pos=" + pos + " type=" + item.PowerItemType
                + " powered=" + item.IsPowered + " required=" + item.RequiredPower
                + " parent=" + parent + " root=" + (root != null ? root.Position.ToString() : "none")
                + " children=[" + children + "] childCount=" + childCount);
        }

        private static void Container(List<string> args, CommandSenderInfo senderInfo)
        {
            if (args.Count != 3 || !TryParseVec(args, 0, out var pos))
            {
                Say("usage: rsc proto container <x> <y> <z>");
                return;
            }
            if (!TryGetStorage(pos, out var te, out var storage, out string reason))
            {
                Say(reason);
                return;
            }
            var user = ResolveUserId(senderInfo);
            var lockable = storage.lockFeature;
            var lockpick = storage.lockpickFeature;
            var grid = storage.itemGrid;
            Say("container " + pos + " grid=" + grid.ContainerSize.x + "x" + grid.ContainerSize.y
                + " locked=" + (lockable == null ? "none" : lockable.IsLocked().ToString())
                + " owner=" + (lockable?.GetOwner()?.CombinedString ?? "none")
                + " access=" + (user == null ? "n/a" : NativeAccess.CanAccessStorage(te, user).ToString())
                + " jammed=" + storage.isJammed
                + " lockpick=" + (lockpick != null && lockpick.NeedsLockpicking())
                + " lockedServer=" + LockManager.Instance.IsLockedServer(storage));
            for (int i = 0; i < grid.Length; i++)
            {
                var stack = grid[i];
                if (stack == null || stack.IsEmpty())
                    continue;
                var key = ItemKeys.FromItemValue(stack.itemValue);
                Say("  slot " + i + ": '" + (stack.itemValue.ItemClass?.GetItemName() ?? "?")
                    + "' x" + stack.count + " q" + stack.itemValue.Quality
                    + " key=" + ItemKeys.HexPrefix(key));
            }
        }

        private static void Move(List<string> args, CommandSenderInfo senderInfo)
        {
            if (args.Count != 7 || !TryParseVec(args, 0, out var srcPos)
                || !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
                || !TryParseVec(args, 4, out var dstPos))
            {
                Say("usage: rsc proto move <sx> <sy> <sz> <slot> <dx> <dy> <dz>");
                return;
            }
            if (srcPos.Equals(dstPos))
            {
                Say("rejected: source and destination are the same container");
                return;
            }
            if (!TryGetStorage(srcPos, out var srcTe, out var srcFeature, out string reason))
            {
                Say("source: " + reason);
                return;
            }
            if (!TryGetStorage(dstPos, out var dstTe, out var dstFeature, out reason))
            {
                Say("dest: " + reason);
                return;
            }
            var user = ResolveUserId(senderInfo);
            if (user == null || !NativeAccess.CanAccessStorage(srcTe, user))
            {
                Say("rejected: access denied on source container");
                return;
            }
            if (!NativeAccess.CanAccessStorage(dstTe, user))
            {
                Say("rejected: access denied on dest container");
                return;
            }
            if (LockManager.Instance.IsLockedServer(srcFeature))
            {
                Say("rejected: source container is in use");
                return;
            }
            if (LockManager.Instance.IsLockedServer(dstFeature))
            {
                Say("rejected: dest container is in use");
                return;
            }
            var srcGrid = srcFeature.itemGrid;
            if (slot < 0 || slot >= srcGrid.Length)
            {
                Say("rejected: slot " + slot + " out of range 0.." + (srcGrid.Length - 1));
                return;
            }
            var stack = srcGrid[slot];
            if (stack == null || stack.IsEmpty())
            {
                Say("rejected: source slot " + slot + " is empty");
                return;
            }
            var srcKey = ItemKeys.FromItemValue(stack.itemValue);
            int sourceMax = stack.itemValue.ItemClass?.MaxCount ?? stack.count;
            var dstGrid = dstFeature.itemGrid;
            var slots = new List<StackPlacement.Slot>(dstGrid.Length);
            for (int i = 0; i < dstGrid.Length; i++)
            {
                var s = dstGrid[i];
                slots.Add(s == null || s.IsEmpty()
                    ? new StackPlacement.Slot(null, 0, 0)
                    : new StackPlacement.Slot(ItemKeys.FromItemValue(s.itemValue), s.count,
                        s.itemValue.ItemClass?.MaxCount ?? s.count));
            }
            var additions = new List<StackPlacement.Placement>();
            int remainder = StackPlacement.Plan(srcKey, stack.count, sourceMax, slots, additions);
            int moved = stack.count - remainder;
            if (moved <= 0)
            {
                Say("rejected: destination has no room for " + ItemKeys.HexPrefix(srcKey));
                return;
            }
            foreach (var p in additions)
            {
                if (p.IsNewStack)
                    dstGrid[p.Index] = new ItemStack(stack.itemValue.Clone(), p.Added);
                else
                    dstGrid[p.Index].count += p.Added;
            }
            stack.count -= moved;
            if (stack.count <= 0)
                srcGrid[slot] = ItemStack.Empty;
            srcFeature.SetModified();
            dstFeature.SetModified();
            string srcAfter = srcGrid[slot].IsEmpty() ? "empty" : ItemKeys.HexPrefix(ItemKeys.FromItemValue(srcGrid[slot].itemValue));
            var dstKeys = new List<string>(additions.Count);
            foreach (var p in additions)
            {
                var placed = dstGrid[p.Index];
                Storage.ItemKey? placedKey = placed == null || placed.IsEmpty()
                    ? (Storage.ItemKey?)null
                    : ItemKeys.FromItemValue(placed.itemValue);
                dstKeys.Add("dst[" + p.Index + "]="
                    + (placedKey.HasValue ? ItemKeys.HexPrefix(placedKey.Value) : "?")
                    + " identical=" + (placedKey.HasValue && placedKey.Value.Equals(srcKey)));
            }
            Say("moved=" + moved + " remaining=" + remainder
                + " srcKey=" + ItemKeys.HexPrefix(srcKey) + " srcAfter=" + srcAfter
                + " " + string.Join(" ", dstKeys));
        }

        private static void Disk(List<string> args, CommandSenderInfo senderInfo)
        {
            if (args.Count != 1 || (args[0] != "tag" && args[0] != "show"))
            {
                Say("usage: rsc proto disk <tag|show>");
                return;
            }
            var player = ResolvePlayer(senderInfo);
            if (player == null)
            {
                Say("rejected: could not resolve acting player");
                return;
            }
            var held = player.inventory?.holdingItemItemValue;
            if (held == null || held.type == 0)
            {
                Say("no item in held slot");
                return;
            }
            if (args[0] == "show")
            {
                Say("rsc.disk = " + (held.TryGetMetadata(DiskMetadataKey, out string id) ? id : "none"));
                return;
            }
            if (!senderInfo.IsLocalGame)
            {
                Say("rejected: disk tag is restricted to the host's local player; "
                    + "server-side held-item edits may not sync to remote clients");
                return;
            }
            if (!held.TryGetMetadata(DiskMetadataKey, out string existing))
            {
                existing = Guid.NewGuid().ToString("N");
                held.SetMetadata(DiskMetadataKey, existing);
                player.inventory.SetStackAt(player.inventory.holdingItemIdx, player.inventory.holdingItemStack);
                Say("tagged held item '" + held.ItemClass?.GetItemName() + "' rsc.disk=" + existing);
                return;
            }
            Say("held item '" + held.ItemClass?.GetItemName() + "' already has rsc.disk=" + existing);
        }

        private static void Station(List<string> args)
        {
            if (args.Count != 3 || !TryParseVec(args, 0, out var pos))
            {
                Say("usage: rsc proto station <x> <y> <z>");
                return;
            }
            var world = GameManager.Instance.World;
            if (!(world.GetTileEntity(pos) is TileEntityWorkstation ws))
            {
                Say("no workstation tile entity at " + pos);
                return;
            }
            ulong ticks = GameTimer.Instance.ticks;
            ulong elapsed = ticks >= ws.lastTickTime ? ticks - ws.lastTickTime : 0UL;
            string blockName = world.GetBlock(pos).Block?.GetBlockName() ?? "?";
            Say("station '" + blockName + "' burning=" + ws.isBurning
                + " crafting=" + ws.IsCrafting + " userAccessing=" + ws.bUserAccessing
                + " lockedServer=" + LockManager.Instance.IsLockedServer(ws)
                + " burnTotalLeft=" + ws.BurnTotalTimeLeft.ToString("F1", CultureInfo.InvariantCulture)
                + " lastTick=" + ws.lastTickTime + " ticks=" + ticks + " elapsed=" + elapsed);
            for (int i = 0; i < ws.queue.Length; i++)
            {
                var q = ws.queue[i];
                if (q == null || q.Recipe == null)
                    continue;
                Say("  queue[" + i + "] '" + q.Recipe.GetName() + "' mult=" + q.Multiplier
                    + " timeLeft=" + q.CraftingTimeLeft.ToString("F1", CultureInfo.InvariantCulture)
                    + " oneItem=" + q.OneItemCraftTime.ToString("F1", CultureInfo.InvariantCulture)
                    + " quality=" + q.Quality + " starter=" + q.StartingEntityId
                    + " crafting=" + q.IsCrafting);
            }
            for (int i = 0; i < ws.output.Length; i++)
            {
                var s = ws.output[i];
                if (s != null && !s.IsEmpty())
                    Say("  output[" + i + "] '" + (s.itemValue.ItemClass?.GetItemName() ?? "?") + "' x" + s.count);
            }
            if (ws.CraftCompleteList != null)
                foreach (var done in ws.CraftCompleteList)
                    Say("  done '" + done.RecipeName + "' x" + done.RecipeUsedCount
                        + " by=" + done.CrafterEntityID + " xp=" + done.CraftExpGain);
        }

        private static void QueueCmd(List<string> args, EntityPlayer player)
        {
            if (args.Count != 5 || !TryParseVec(args, 0, out var pos)
                || !int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                Say("usage: rsc proto queue <x> <y> <z> <recipeName> <count> (probe: ingredients not consumed)");
                return;
            }
            var world = GameManager.Instance.World;
            if (!(world.GetTileEntity(pos) is TileEntityWorkstation ws))
            {
                Say("no workstation tile entity at " + pos);
                return;
            }
            if (LockManager.Instance.IsLockedServer(ws))
            {
                Say("rejected: station is in use");
                return;
            }
            var recipe = CraftingManager.GetRecipe(args[3]);
            if (recipe == null)
            {
                Say("rejected: unknown recipe '" + args[3] + "'");
                return;
            }
            var block = world.GetBlock(pos).Block;
            string[] areas = StationCraftingAreas(block);
            if (string.IsNullOrEmpty(recipe.craftingArea))
            {
                Say("rejected: recipe '" + args[3] + "' needs no station");
                return;
            }
            if (areas == null || !areas.Contains(recipe.craftingArea))
            {
                Say("rejected: recipe area '" + recipe.craftingArea + "' not offered by '"
                    + (block?.GetBlockName() ?? "?") + "' (areas: "
                    + (areas == null ? "none" : string.Join(",", areas)) + ")");
                return;
            }
            int slotIndex = -1;
            for (int i = ws.queue.Length - 1; i >= 0; i--)
            {
                if (ws.queue[i] == null || ws.queue[i].Recipe == null)
                {
                    slotIndex = i;
                    break;
                }
            }
            if (slotIndex < 0)
            {
                Say("rejected: queue full");
                return;
            }
            int quality = 1;
            string qualityNote = "fallback";
            try
            {
                quality = recipe.GetCraftingTier(player);
                qualityNote = "native";
            }
            catch (Exception)
            {
            }
            var entry = ws.queue[slotIndex] ?? (ws.queue[slotIndex] = new RecipeQueueItem());
            entry.Recipe = recipe;
            entry.Multiplier = (short)Math.Max(1, Math.Min(count, short.MaxValue));
            entry.CraftingTimeLeft = recipe.craftingTime;
            entry.OneItemCraftTime = recipe.craftingTime;
            entry.Quality = (byte)Math.Max(0, Math.Min(quality, byte.MaxValue));
            entry.StartingEntityId = player.entityId;
            entry.IsCrafting = slotIndex == ws.queue.Length - 1;
            ws.SetModified();
            Say("queued '" + recipe.GetName() + "' x" + entry.Multiplier + " slot=" + slotIndex
                + " quality=" + entry.Quality + "(" + qualityNote + ")"
                + " starter=" + entry.StartingEntityId + " (probe: ingredients not consumed)");
        }

        // Same derivation the recipe-list UI uses: the block's Workstation
        // CraftingAreaRecipes property (minus "player"), else the block name.
        private static string[] StationCraftingAreas(Block block)
        {
            if (block == null)
                return null;
            string name = block.GetBlockName();
            if (block.Properties.Contains("Workstation", "CraftingAreaRecipes"))
            {
                string prop = block.Properties.GetString("Workstation", "CraftingAreaRecipes");
                return prop.Replace("player", "").Replace(", ", ",").Replace(" ,", ",")
                    .Replace(" , ", ",").Split(',');
            }
            return new[] { name };
        }

        private static void Net(List<string> args)
        {
            if (args.Count != 3 || !TryParseVec(args, 0, out var pos))
            {
                Say("usage: rsc proto net <x> <y> <z>");
                return;
            }
            var world = GameManager.Instance.World;
            var block = world.GetBlock(pos).Block;
            if (!Topology.RscBlocks.TryGetRole(block, out var kind, out var tier))
            {
                Say("no RSC node at " + pos);
                return;
            }
            var registry = Topology.ServerTopology.Snapshot();
            var id = Topology.RscBlocks.ToNodeId(pos);
            var status = registry.GetStatus(id);
            var item = Topology.ServerTopology.PowerItemAt(pos);
            var line = "net " + pos + " role=" + kind
                + (tier.HasValue ? " tier=" + tier.Value : "")
                + (item != null ? " powered=" + item.IsPowered : "");
            if (status.State == Core.NodeState.Connected && status.Controller.HasValue)
            {
                var controllerId = status.Controller.Value;
                var controllerPos = Topology.RscBlocks.FromNodeId(controllerId);
                var controllerItem = Topology.ServerTopology.PowerItemAt(controllerPos);
                var members = registry.Members(controllerId);
                Say(line + " state=Connected controller=" + controllerPos
                    + " controllerTier=" + registry.TierOf(controllerId)
                    + " controllerPowered=" + (controllerItem?.IsPowered.ToString() ?? "?")
                    + " members=" + members.Count);
                int shown = 0;
                foreach (var member in members)
                {
                    if (shown++ >= 30)
                    {
                        Say("  ... +" + (members.Count - 30) + " more");
                        break;
                    }
                    Say("  member " + Topology.RscBlocks.FromNodeId(member));
                }
            }
            else
            {
                Say(line + " state=" + status.State);
            }
        }

        // Coordinates helper: reports the block under the host's crosshair and,
        // for RSC nodes, chains the net and power reports.
        private static void Look(EntityPlayer player)
        {
            if (!(player is EntityPlayerLocal local) || local.HitInfo == null || !local.HitInfo.bHitValid)
            {
                Say("look: no block under the crosshair (host's local player only)");
                return;
            }
            var pos = local.HitInfo.hit.blockPos;
            var world = GameManager.Instance.World;
            string blockName = world.GetBlock(pos).Block?.GetBlockName() ?? "?";
            Say("look '" + blockName + "' at " + pos.x + " " + pos.y + " " + pos.z);
            var coords = new List<string>
            {
                pos.x.ToString(CultureInfo.InvariantCulture),
                pos.y.ToString(CultureInfo.InvariantCulture),
                pos.z.ToString(CultureInfo.InvariantCulture)
            };
            if (Topology.RscBlocks.TryGetRole(world.GetBlock(pos).Block, out _, out _))
            {
                Net(coords);
                Power(coords);
            }
            else if (world.GetTileEntity(pos) is TileEntityWorkstation)
                Station(coords);
        }

        private static void Observe(List<string> args)
        {
            if (args.Count != 4 || !TryParseVec(args, 0, out var pos)
                || !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int viewDim))
            {
                Say("usage: rsc proto observe <x> <y> <z> <viewDim>");
                return;
            }
            var chunkManager = GameManager.Instance.World?.m_ChunkManager;
            if (chunkManager == null)
            {
                Say("rejected: no world/chunk manager");
                return;
            }
            viewDim = Math.Max(1, Math.Min(viewDim, 5));
            var observer = chunkManager.AddChunkObserver(
                new Vector3(pos.x, pos.y, pos.z), false, viewDim, -1);
            Observers[observer.id] = observer;
            Say("observer id=" + observer.id + " pos=" + pos + " viewDim=" + viewDim
                + " (no mesh, no client target)");
        }

        private static void Unobserve(List<string> args)
        {
            if (args.Count != 1
                || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                Say("usage: rsc proto unobserve <id>");
                return;
            }
            if (!Observers.TryGetValue(id, out var observer))
            {
                Say("rejected: unknown observer id " + id);
                return;
            }
            GameManager.Instance.World?.m_ChunkManager?.RemoveChunkObserver(observer);
            Observers.Remove(id);
            Say("observer id=" + id + " removed");
        }

        private static void ListObservers()
        {
            if (Observers.Count == 0)
            {
                Say("no active observers");
                return;
            }
            foreach (var pair in Observers)
                Say("observer id=" + pair.Key + " pos=" + pair.Value.position + " viewDim=" + pair.Value.viewDim);
        }
    }
}
