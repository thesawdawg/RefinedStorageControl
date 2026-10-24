using System.Collections.Generic;
using System.Globalization;
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
            "rsc net withdraw <x y z> <#> <count> <cx cy cz> - move up to one stack of list entry # into container cx cy cz";

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
