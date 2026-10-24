using System.Collections.Generic;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Persistence;

namespace RefinedStorageControl.Game.Crafting
{
    // `rsc craft jobs`: what terminal crafting is doing right now (host only).
    internal static class CraftCommands
    {
        public const string Usage =
            "rsc craft jobs - terminal crafting: countdowns, queued jobs, items waiting, keepalive and banked XP";

        public static void Dispatch(List<string> args)
        {
            if (!ConnectionManager.Instance.IsServer)
            {
                Say("rejected: craft commands are host/server only");
                return;
            }
            var store = CraftFile.Store;
            if (store == null)
            {
                Say("no craft job store (no game running?)");
                return;
            }
            if (args.Count != 1 || args[0].ToLowerInvariant() != "jobs")
            {
                Say(Usage);
                return;
            }
            foreach (string line in CraftService.Report())
                Say(line);
        }

        public static string Where(long key)
        {
            PositionKey.Unpack(key, out int x, out int y, out int z);
            return x + " " + y + " " + z;
        }

        private static void Say(string line) => SdtdConsole.Instance.Output("[RSC] " + line);
    }
}
