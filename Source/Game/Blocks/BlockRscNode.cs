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

        // DISK-04/05: tell the removal hook that this removal is a destruction.
        public override DestroyedResult OnBlockDestroyedBy(WorldBase _world, BlockValueRef _bvRef, BlockValue _blockValue,
            int _entityId, bool _bUseHarvestTool)
        {
            if (_bvRef.TryGetBlockPos(out var pos))
                Persistence.RemovalHooks.MarkDestroyed(pos);
            return base.OnBlockDestroyedBy(_world, _bvRef, _blockValue, _entityId, _bUseHarvestTool);
        }

        public override DestroyedResult OnBlockDestroyedByExplosion(WorldBase _world, BlockValueRef _bvRef,
            BlockValue _blockValue, int _playerThatStartedExpl)
        {
            if (_bvRef.TryGetBlockPos(out var pos))
                Persistence.RemovalHooks.MarkDestroyed(pos);
            return base.OnBlockDestroyedByExplosion(_world, _bvRef, _blockValue, _playerThatStartedExpl);
        }

        // A collapsing block is treated as destroyed too.
        public override void OnBlockStartsToFall(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue)
        {
            Persistence.RemovalHooks.MarkDestroyed(_blockPos);
            base.OnBlockStartsToFall(_world, _blockPos, _blockValue);
        }

        // DISK-09: a cabinet can be picked up only when it holds no disks. The host
        // knows directly; a client asks the server and retries. Should a stale
        // answer still let a loaded cabinet through, the server drops its disks
        // intact, so nothing is lost.
        public override bool takeItemWithTimerCanTake(Vector3i _blockPos, EntityAlive _player)
        {
            if (!RscBlocks.TryGetRole(this, out var kind, out _) || kind != NodeKind.DriveCabinet)
                return base.takeItemWithTimerCanTake(_blockPos, _player);
            bool? loaded;
            if (ConnectionManager.Instance.IsServer)
                loaded = !(Persistence.DiskFile.Store?.IsEmpty(PositionKey.Pack(_blockPos.x, _blockPos.y, _blockPos.z)) ?? true);
            else
            {
                loaded = Net.ClientSession.CabinetLoaded(_blockPos);
                if (!loaded.HasValue)
                {
                    Net.ClientSession.ProbeCabinet(_blockPos);
                    Deny(_player, "rscCabinetChecking");
                    return false;
                }
            }
            if (loaded.Value)
            {
                Deny(_player, "rscCabinetNotEmpty");
                return false;
            }
            return base.takeItemWithTimerCanTake(_blockPos, _player);
        }

        private static void Deny(EntityAlive player, string key)
        {
            if (player is EntityPlayerLocal local)
                GameManager.ShowTooltip(local, Localization.Get(key), string.Empty, "ui_denied");
        }

        private bool Opens(out NodeKind kind)
        {
            return RscBlocks.TryGetRole(this, out kind, out _)
                && (kind == NodeKind.Terminal || kind == NodeKind.Interface || kind == NodeKind.DriveCabinet);
        }
    }
}
