using RefinedStorageControl.Core;

namespace RefinedStorageControl.Game.Providers
{
    // PHY-01: a view over a real vanilla container's own ItemStackGrid. Nothing
    // is copied; every change is written back to the container and broadcast
    // with SetModified. Only player-placed containers are eligible (PHY-02), so
    // the network never touches unopened or respawning loot.
    internal sealed class ContainerProvider : GridEndpoint
    {
        private ContainerProvider(Vector3i position, TileEntityComposite te, TEFeatureStorage storage)
        {
            Position = position;
            Te = te;
            Storage = storage;
            SortKey = PositionKey.Pack(position.x, position.y, position.z);
        }

        public Vector3i Position { get; }
        public TileEntityComposite Te { get; }
        public TEFeatureStorage Storage { get; }
        public long SortKey { get; }
        public override ItemStackGrid Grid => Storage.itemGrid;
        public override string Label => "container at " + Position;

        // The interface whose priority and filter apply; set when the network
        // collects its providers.
        public NodeId? Interface { get; set; }

        // Resolves child blocks of multi-block containers to their parent.
        public static bool TryResolve(World world, Vector3i pos, out ContainerProvider provider, out string reason)
        {
            provider = null;
            var blockValue = world.GetBlock(pos);
            if (blockValue.ischild)
                pos = blockValue.Block.multiBlockPos.GetParentPos(pos, blockValue);
            if (!(world.GetTileEntity(pos) is TileEntityComposite te))
            {
                reason = "no container at " + pos;
                return false;
            }
            var storage = te.GetFeature<TEFeatureStorage>();
            if (storage == null)
            {
                reason = "no storage at " + pos;
                return false;
            }
            if (!te.PlayerPlaced)
            {
                reason = "not a player-placed container at " + pos;
                return false;
            }
            provider = new ContainerProvider(pos, te, storage);
            reason = null;
            return true;
        }

        public override bool CanAccess(PlatformUserIdentifierAbs user) => NativeAccess.CanAccessStorage(Te, user);

        // A player has the container open; their client will upload its whole
        // state, so host edits now could be overwritten or duplicate items (A01).
        public override bool InUse => LockManager.Instance.IsLockedServer(Storage);

        public override void MarkModified() => Storage.SetModified();
    }
}
