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
    }
}
