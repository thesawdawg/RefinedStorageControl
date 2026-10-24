using System;
using System.Collections.Generic;

namespace RefinedStorageControl.Storage
{
    // DISK-04/05 (F01 defaults, configurable): when a drive cabinet is destroyed,
    // each installed disk is rolled once and survives with DiskSurvivalChance;
    // when an interface or cabinet is destroyed, its deposit filter is lost with
    // FilterLossChance. `roll` returns a value in [0, 1).
    public static class DestructionRules
    {
        public const double DiskSurvivalChance = 0.5;
        public const double FilterLossChance = 0.5;

        public static void RollDisks(IEnumerable<Guid> disks, double survivalChance, Func<double> roll,
            List<Guid> survivors, List<Guid> destroyed)
        {
            if (survivalChance < 0 || survivalChance > 1)
                throw new ArgumentOutOfRangeException(nameof(survivalChance), survivalChance, "Chance must be 0-1.");
            foreach (var disk in disks)
            {
                if (roll() < survivalChance)
                    survivors.Add(disk);
                else
                    destroyed.Add(disk);
            }
        }

        public static bool LosesFilter(double lossChance, Func<double> roll)
        {
            if (lossChance < 0 || lossChance > 1)
                throw new ArgumentOutOfRangeException(nameof(lossChance), lossChance, "Chance must be 0-1.");
            return roll() < lossChance;
        }
    }
}
