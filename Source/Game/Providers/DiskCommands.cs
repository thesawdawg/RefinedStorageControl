using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Persistence;

namespace RefinedStorageControl.Game.Providers
{
    // `rsc disk ...`: host/admin tools for digital storage (ACCESS-04 override).
    internal static class DiskCommands
    {
        public const string Usage =
            "rsc disk list - every disk in this world: id, tier, use, owner, where it is installed\n" +
            "rsc disk owner <diskIdPrefix> <player name or id> - make that player the disk's owner";

        public static void Dispatch(List<string> args)
        {
            if (!ConnectionManager.Instance.IsServer)
            {
                Say("rejected: disk commands are host/server only");
                return;
            }
            var store = DiskFile.Store;
            if (store == null)
            {
                Say("no disk store (no game running?)");
                return;
            }
            string verb = args.Count > 0 ? args[0].ToLowerInvariant() : "";
            if (verb == "list" && args.Count == 1)
            {
                Say(store.DiskCount + " disks, world " + store.WorldId.ToString("N"));
                foreach (var disk in store.Disks.OrderBy(d => d.Id))
                {
                    var where = store.FindCabinet(disk.Id);
                    string place = "not installed";
                    if (where.HasValue)
                    {
                        PositionKey.Unpack(where.Value.cabinet, out int x, out int y, out int z);
                        place = "cabinet " + x + " " + y + " " + z + " slot " + (where.Value.slot + 1);
                    }
                    Say("  " + disk.Id.ToString("N").Substring(0, 8) + " " + disk.Tier + " " + disk.Used + "/" + disk.Capacity
                        + " owner " + CabinetService.NameOf(disk.Acl.OwnerId) + ", " + place);
                }
                return;
            }
            if (verb == "owner" && args.Count >= 3)
            {
                string who = string.Join(" ", args.Skip(2));
                string id = ResolvePlayer(who);
                if (id == null)
                {
                    Say("rejected: no known player named or with id '" + who + "'");
                    return;
                }
                string error = CabinetService.ForceOwner(args[1], id);
                Say(error == null ? "disk " + args[1] + " now belongs to " + CabinetService.NameOf(id) : "rejected: " + error);
                return;
            }
            SdtdConsole.Instance.Output(Usage);
        }

        private static string ResolvePlayer(string who)
        {
            var players = GameManager.Instance.persistentPlayers?.Players;
            if (players == null)
                return null;
            foreach (var pair in players)
            {
                string id = pair.Value?.PrimaryId?.CombinedString ?? pair.Key?.CombinedString;
                if (id == who || string.Equals(pair.Value?.PlayerName?.DisplayName, who, System.StringComparison.OrdinalIgnoreCase))
                    return id;
            }
            return null;
        }

        private static void Say(string message)
        {
            SdtdConsole.Instance.Output("[RSC disk] " + message);
            RscLog.Out("disk " + message);
        }
    }
}
