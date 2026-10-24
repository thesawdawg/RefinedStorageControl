using System.Collections.Generic;
using System.Globalization;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Providers
{
    // `rsc net ...`: console front end for TransferService until the terminal UI
    // exists. Host/server only; the acting user is the command sender, so native
    // container access is checked for that player.
    internal static class NetCommands
    {
        // Last `list` result per user, so withdraw can refer to entries by index.
        private static readonly Dictionary<string, List<ItemKey>> LastList = new Dictionary<string, List<ItemKey>>();

        public const string Usage =
            "rsc net list <x y z> - items this player can reach through the network of block x y z\n" +
            "rsc net deposit <x y z> <cx cy cz> <slot> - move container cx cy cz slot into the network\n" +
            "rsc net withdraw <x y z> <#> <count> <cx cy cz> - move up to one stack of list entry # into container cx cy cz\n" +
            "rsc net config <x y z> - show an interface's priority, filter and containers\n" +
            "rsc net priority <x y z> <0-100> - set an interface's priority (lower is preferred for deposits)\n" +
            "rsc net filter <x y z> none|allow|block [itemName ...] - set an interface's deposit filter";

        public static void Dispatch(List<string> args, CommandSenderInfo senderInfo)
        {
            if (!ConnectionManager.Instance.IsServer)
            {
                Say("rejected: network commands are host/server only");
                return;
            }
            var user = ResolveUserId(senderInfo);
            if (user == null)
            {
                Say("rejected: could not resolve acting player");
                return;
            }
            string verb = args.Count > 0 ? args[0].ToLowerInvariant() : "";
            switch (verb)
            {
                case "list" when args.Count == 4 && TryVec(args, 1, out var pos):
                    List(user, pos);
                    return;
                case "deposit" when args.Count == 8 && TryVec(args, 1, out var pos)
                    && TryVec(args, 4, out var cpos) && TryInt(args[7], out int slot):
                    Deposit(user, pos, cpos, slot);
                    return;
                case "withdraw" when args.Count == 9 && TryVec(args, 1, out var pos)
                    && TryInt(args[4], out int index) && TryInt(args[5], out int count) && TryVec(args, 6, out var cpos):
                    Withdraw(user, pos, index, count, cpos);
                    return;
                case "config" when args.Count == 4 && TryVec(args, 1, out var pos):
                    ShowConfig(pos);
                    return;
                case "priority" when args.Count == 5 && TryVec(args, 1, out var pos) && TryInt(args[4], out int priority):
                    SetPriority(pos, priority);
                    return;
                case "filter" when args.Count >= 5 && TryVec(args, 1, out var pos):
                    SetFilter(pos, args[4].ToLowerInvariant(), args.GetRange(5, args.Count - 5));
                    return;
            }
            SdtdConsole.Instance.Output(Usage);
        }

        public static void Reset() => LastList.Clear();

        private static void List(PlatformUserIdentifierAbs user, Vector3i pos)
        {
            if (!TransferService.TryList(user, pos, out var entries, out string reason))
            {
                Say("rejected: " + reason);
                return;
            }
            var keys = new List<ItemKey>(entries.Count);
            Say(entries.Count + " item kinds reachable");
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                keys.Add(e.Key);
                Say("  #" + i + " '" + TransferService.Name(e.Sample) + "' x" + e.Count
                    + (e.Sample.HasQuality ? " q" + e.Sample.Quality : "")
                    + " in " + e.Providers + " container(s) key=" + ItemKeys.HexPrefix(e.Key));
            }
            LastList[user.CombinedString] = keys;
        }

        private static void Deposit(PlatformUserIdentifierAbs user, Vector3i pos, Vector3i cpos, int slot)
        {
            if (!ContainerProvider.TryResolve(GameManager.Instance.World, cpos, out var source, out string reason))
            {
                Say("rejected: " + reason);
                return;
            }
            Report(TransferService.Deposit(user, pos, source, slot), "deposited");
        }

        private static void Withdraw(PlatformUserIdentifierAbs user, Vector3i pos, int index, int count, Vector3i cpos)
        {
            if (!LastList.TryGetValue(user.CombinedString, out var keys) || index < 0 || index >= keys.Count)
            {
                Say("rejected: run 'rsc net list' first and use an index from it");
                return;
            }
            if (!ContainerProvider.TryResolve(GameManager.Instance.World, cpos, out var dest, out string reason))
            {
                Say("rejected: " + reason);
                return;
            }
            Report(TransferService.Withdraw(user, pos, keys[index], count, dest), "withdrew");
        }

        // Configuration is host-console only until the terminal UI adds its own
        // permission checks; any player allowed to run console commands can use it.
        private static void ShowConfig(Vector3i pos)
        {
            if (!IsInterface(pos))
                return;
            var config = Persistence.NodeConfigFile.Store.Get(Key(pos));
            Say("interface " + pos + " priority=" + config.Priority + " filter=" + Describe(config.Filter));
            var containers = TransferService.ContainersAround(GameManager.Instance.World, pos);
            if (containers.Count == 0)
                Say("  no player-placed container touching this interface");
            foreach (var c in containers)
                Say("  serves container at " + c.Position);
        }

        private static void SetPriority(Vector3i pos, int priority)
        {
            if (!IsInterface(pos))
                return;
            if (priority < ProviderRouting.MinPriority || priority > ProviderRouting.MaxPriority)
            {
                Say("rejected: priority must be 0-100");
                return;
            }
            var store = Persistence.NodeConfigFile.Store;
            store.Set(Key(pos), new NodeConfig(priority, store.Get(Key(pos)).Filter));
            Say("interface " + pos + " priority=" + priority);
        }

        private static void SetFilter(Vector3i pos, string mode, List<string> names)
        {
            if (!IsInterface(pos))
                return;
            FilterMode filterMode;
            switch (mode)
            {
                case "none": filterMode = FilterMode.None; break;
                case "allow": filterMode = FilterMode.Allow; break;
                case "block": filterMode = FilterMode.Block; break;
                default:
                    Say("rejected: filter mode must be none, allow or block");
                    return;
            }
            if (filterMode == FilterMode.None)
                names.Clear();
            else if (names.Count == 0)
            {
                Say("rejected: list at least one item name");
                return;
            }
            foreach (var name in names)
                if (ItemClass.GetItemClass(name) == null)
                {
                    Say("rejected: unknown item '" + name + "' (names are case-sensitive)");
                    return;
                }
            var store = Persistence.NodeConfigFile.Store;
            var filter = new ItemFilter(filterMode, names);
            store.Set(Key(pos), new NodeConfig(store.Get(Key(pos)).Priority, filter));
            Say("interface " + pos + " filter=" + Describe(filter));
        }

        private static bool IsInterface(Vector3i pos)
        {
            var block = GameManager.Instance.World.GetBlock(pos).Block;
            if (Topology.RscBlocks.TryGetRole(block, out var kind, out _) && kind == Core.NodeKind.Interface)
                return true;
            Say("rejected: no storage interface at " + pos);
            return false;
        }

        private static long Key(Vector3i pos) => Core.PositionKey.Pack(pos.x, pos.y, pos.z);

        private static string Describe(ItemFilter filter) =>
            filter.Mode == FilterMode.None
                ? "none"
                : filter.Mode.ToString().ToLowerInvariant() + " [" + string.Join(", ", filter.Names) + "]";

        private static void Report(TransferService.Result result, string verb)
        {
            if (!result.Ok)
                Say("rejected: " + result.Reason);
            else
                Say(verb + " " + result.Moved + " using " + result.Transfers + " transfer(s)"
                    + (result.Reason != null ? "; " + result.Reason : ""));
        }

        private static void Say(string message)
        {
            SdtdConsole.Instance.Output("[RSC net] " + message);
            RscLog.Out("net " + message);
        }

        private static PlatformUserIdentifierAbs ResolveUserId(CommandSenderInfo senderInfo)
        {
            if (senderInfo.IsLocalGame)
                return Platform.PlatformManager.InternalLocalUserIdentifier;
            return senderInfo.RemoteClientInfo?.InternalId;
        }

        private static bool TryInt(string s, out int value) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryVec(List<string> args, int start, out Vector3i pos)
        {
            pos = default;
            if (args.Count < start + 3 || !TryInt(args[start], out int x)
                || !TryInt(args[start + 1], out int y) || !TryInt(args[start + 2], out int z))
                return false;
            pos = new Vector3i(x, y, z);
            return true;
        }
    }
}
