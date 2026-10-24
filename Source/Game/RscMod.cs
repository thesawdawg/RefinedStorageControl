using System;
using HarmonyLib;

namespace RefinedStorageControl.Game
{
    public sealed class RscMod : IModApi
    {
        public void InitMod(Mod mod)
        {
            ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
            try
            {
                new Harmony("refinedstoragecontrol").PatchAll(typeof(RscMod).Assembly);
                RscLog.Out("Harmony patches applied");
            }
            catch (Exception e)
            {
                RscLog.Error("Harmony patching failed: " + e);
            }
            RscLog.Out("Refined Storage Control "
                + typeof(RscMod).Assembly.GetName().Version
                + " initialized");
        }

        private static void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData data)
        {
            Prototypes.ProtoCommands.OnWorldShuttingDown();
        }
    }
}
