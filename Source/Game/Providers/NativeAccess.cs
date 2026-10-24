namespace RefinedStorageControl.Game.Providers
{
    // ACCESS-01/02: the host must apply the same permission rule the client
    // applies in TEFeatureStorage.OnBlockActivated, because the server does not
    // check it. An unlocked container is natively open to everyone.
    internal static class NativeAccess
    {
        public static bool CanAccessStorage(TileEntityComposite te, PlatformUserIdentifierAbs user)
        {
            var storage = te?.GetFeature<TEFeatureStorage>();
            if (storage == null || user == null)
                return false;
            var lockable = storage.lockFeature;
            var lockpick = storage.lockpickFeature;
            return !(lockable != null && lockable.IsLocked() && !lockable.IsUserAllowed(user))
                && !storage.isJammed
                && !(lockpick != null && lockpick.NeedsLockpicking());
        }
    }
}
