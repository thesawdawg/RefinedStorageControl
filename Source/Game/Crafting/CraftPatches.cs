using System.Collections.Generic;
using HarmonyLib;
using RefinedStorageControl.Crafting;

namespace RefinedStorageControl.Game.Crafting
{
    // The native station merges a finished craft into any waiting record for
    // the same item, whoever crafted it. A terminal job's records must stay
    // apart from players' own (CRAFT-08), so a record carrying a job tag only
    // merges with records of that same job. Player-only merging is unchanged.
    // Runs wherever the craft finishes: on the host, or on the client of a
    // player who has the station open.
    [HarmonyPatch(typeof(TileEntityWorkstation), nameof(TileEntityWorkstation.AddCraftComplete))]
    internal static class AddCraftCompletePatch
    {
        private static bool Prefix(TileEntityWorkstation __instance, int crafterEntityID, ItemValue itemCrafted,
            string recipeName, string itemScrapped, int craftExpGain, int craftedCount)
        {
            if (__instance.CraftCompleteList == null)
                __instance.CraftCompleteList = new List<CraftCompleteData>();
            var list = __instance.CraftCompleteList;
            bool tagged = CraftTags.IsTag(crafterEntityID);
            foreach (var entry in list)
            {
                if (entry.CraftedItemStack.itemValue.GetItemId() != itemCrafted.GetItemId() || entry.ItemScrapped != itemScrapped)
                    continue;
                bool entryTagged = CraftTags.IsTag(entry.CrafterEntityID);
                if ((tagged || entryTagged) && entry.CrafterEntityID != crafterEntityID)
                    continue;
                entry.CraftedItemStack.count += craftedCount;
                __instance.setModified();
                return false;
            }
            list.Add(new CraftCompleteData(crafterEntityID, new ItemStack(itemCrafted, craftedCount), recipeName, itemScrapped,
                craftExpGain, 1));
            __instance.setModified();
            return false;
        }
    }
}
