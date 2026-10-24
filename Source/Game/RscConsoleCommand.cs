using System;
using System.Collections.Generic;

namespace RefinedStorageControl.Game
{
    // Diagnostics and console-driven network transfers. The game auto-registers
    // ConsoleCmdAbstract subclasses found in mod assemblies.
    public sealed class RscConsoleCommand : ConsoleCmdAbstract
    {
        public override string[] getCommands() => new[] { "rsc" };

        public override string getDescription() => "Refined Storage Control diagnostics and network transfers";

        public override string getHelp() =>
            "rsc version - print assembly and build module version\n" +
            Providers.NetCommands.Usage + "\n" +
            Providers.DiskCommands.Usage + "\n" +
            Crafting.CraftCommands.Usage + "\n" +
            "rsc journal [lines] [text] - recent transfers, disk, crafting and admin events (default 30), optionally only lines containing text\n" +
            "rsc settings - the values in use from Settings.xml\n" +
            "rsc proto <look|power|container|move|disk|station|queue|net|observe|unobserve|observers> - P0 prototypes (host only)";

        // The game passes only the arguments after the command name
        // (SdtdConsole executes with list.GetRange(1, ...)).
        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            if (_params != null && _params.Count > 0)
            {
                switch (_params[0].ToLowerInvariant())
                {
                    case "version":
                        var assembly = typeof(RscConsoleCommand).Assembly;
                        SdtdConsole.Instance.Output("[RSC] version " + assembly.GetName().Version
                            + " module " + assembly.ManifestModule.ModuleVersionId);
                        return;
                    case "disk":
                        Providers.DiskCommands.Dispatch(_params.GetRange(1, _params.Count - 1));
                        return;
                    case "journal":
                        Journal(_params);
                        return;
                    case "settings":
                        foreach (var pair in Config.RscSettings.Current.Values())
                            SdtdConsole.Instance.Output("[RSC] " + pair.Key + " = " + pair.Value);
                        return;
                    case "craft":
                        Crafting.CraftCommands.Dispatch(_params.GetRange(1, _params.Count - 1));
                        return;
                    case "net":
                        Providers.NetCommands.Dispatch(_params.GetRange(1, _params.Count - 1), _senderInfo);
                        return;
                    case "proto":
                        Prototypes.ProtoCommands.Dispatch(_params.GetRange(1, _params.Count - 1), _senderInfo);
                        return;
                }
            }
            SdtdConsole.Instance.Output(getHelp());
        }

        private static void Journal(List<string> args)
        {
            int n = 30;
            int textFrom = 1;
            if (args.Count > 1 && int.TryParse(args[1], out int parsed))
            {
                n = System.Math.Max(1, System.Math.Min(parsed, 1000));
                textFrom = 2;
            }
            string filter = args.Count > textFrom ? string.Join(" ", args.GetRange(textFrom, args.Count - textFrom)) : null;
            var lines = RscJournal.Last(n, filter);
            if (lines.Count == 0)
                SdtdConsole.Instance.Output("[RSC] journal: nothing" + (filter != null ? " matching '" + filter + "'" : ""));
            foreach (string line in lines)
                SdtdConsole.Instance.Output("[RSC] " + line);
        }
    }
}
