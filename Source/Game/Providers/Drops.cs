using UnityEngine;

namespace RefinedStorageControl.Game.Providers
{
    // Items RSC has to put on the ground: overflow a backpack could not take, and
    // disks from a removed cabinet. The vanilla default lifetime (60 s) is too
    // short for that, so drops last an hour of real time (DropLifetimeSeconds).
    internal static class Drops
    {
        private static float LifetimeSeconds => (float)Config.RscSettings.Current.DropLifetimeSeconds;

        public static void Drop(ItemValue item, int count, Vector3 position, int entityId = -1)
        {
            if (item == null || item.IsEmpty() || count <= 0)
                return;
            GameManager.Instance.ItemDropServer(new ItemStack(item.Clone(), count), position, Vector3.zero, entityId,
                LifetimeSeconds);
        }
    }
}
