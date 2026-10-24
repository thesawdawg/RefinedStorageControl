using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Topology;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.Blocks
{
    // Block class for every RSC node (XML: Class="RefinedStorageControl.Game.Blocks.RscNode,
    // RefinedStorageControl"). It keeps BlockPowered's vanilla tile entity and
    // power item (P0 finding 2) and only adds an "open" activation for terminals,
    // interfaces and drive cabinets. Host and co-op clients use the same windows; clients
    // talk to the server through the RSC network packages.
    [Preserve]
    public class BlockRscNode : BlockPowered
    {
        private const string OpenCommand = "rsc_open";

        private BlockActivationCommand[] _commands;

        public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue,
            Vector3i _blockPos, EntityAlive _entityFocusing)
        {
            var inherited = base.GetBlockActivationCommands(_world, _blockValue, _blockPos, _entityFocusing);
            if (!Opens(out var kind))
                return inherited;
            if (_commands == null || _commands.Length != inherited.Length + 1)
                _commands = new BlockActivationCommand[inherited.Length + 1];
            _commands[0] = new BlockActivationCommand(OpenCommand, kind == NodeKind.Terminal ? "hand" : "tool", true);
            for (int i = 0; i < inherited.Length; i++)
                _commands[i + 1] = inherited[i];
            return _commands;
        }

        public override string GetActivationText(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos,
            EntityAlive _entityFocusing)
        {
            if (!Opens(out var kind) || !(_entityFocusing is EntityPlayerLocal player))
                return base.GetActivationText(_world, _blockValue, _blockPos, _entityFocusing);
            string key = player.playerInput.Activate.GetBindingXuiMarkupString()
                + player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();
            return string.Format(Localization.Get(kind == NodeKind.Interface ? "rscPromptInterface" : "rscPromptTerminal"),
                key, GetLocalizedBlockName());
        }

        public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos,
            BlockValue _blockValue, EntityPlayerLocal _player)
        {
            if (_commandName != OpenCommand || !Opens(out var kind))
                return base.OnBlockActivated(_commandName, _world, _blockPos, _blockValue, _player);
            if (kind == NodeKind.Terminal)
                UI.XUiC_RscTerminal.Open(_player, _blockPos);
            else if (kind == NodeKind.DriveCabinet)
                UI.XUiC_RscCabinet.Open(_player, _blockPos);
            else
                UI.XUiC_RscInterface.Open(_player, _blockPos);
            return true;
        }

        private bool Opens(out NodeKind kind)
        {
            return RscBlocks.TryGetRole(this, out kind, out _)
                && (kind == NodeKind.Terminal || kind == NodeKind.Interface || kind == NodeKind.DriveCabinet);
        }
    }
}
