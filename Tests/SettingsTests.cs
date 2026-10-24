using System;
using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.Config;
using RefinedStorageControl.Core;
using RefinedStorageControl.Diagnostics;
using RefinedStorageControl.Routing;
using static Harness;

internal static class SettingsTests
{
    public static void Run()
    {
        Settings();
        JournalRing();
    }

    private static KeyValuePair<string, string> E(string k, string v) => new KeyValuePair<string, string>(k, v);

    private static void Settings()
    {
        var defaults = new RscSettings();
        Check(defaults.SimpleTransfersPerSecond == 2 && defaults.AdvancedTransfersPerSecond == 20
            && defaults.DiskSurvivalChance == 0.5 && defaults.FilterLossChance == 0.5
            && defaults.CraftCountdownSeconds == 5 && defaults.MaxCraftsPerRequest == 999,
            "Defaults must match the decided values (F01, F12, CRAFT-04).");

        var warnings = new List<string>();
        var s = RscSettings.Parse(new[]
        {
            E("simpleTransfersPerSecond", "4"), E("DiskSurvivalChance", "0.75"), E("CraftCountdownSeconds", "3.5")
        }, warnings);
        Check(warnings.Count == 0 && s.SimpleTransfersPerSecond == 4 && s.DiskSurvivalChance == 0.75
            && s.CraftCountdownSeconds == 3.5 && s.AdvancedTransfersPerSecond == 20,
            "Valid entries apply (names ignore case); others keep their defaults.");

        warnings.Clear();
        var bad = RscSettings.Parse(new[]
        {
            E("Nope", "1"), E("FilterLossChance", "1.5"), E("MaxCraftsPerRequest", "2.5"), E("JournalLines", "abc"),
            E("SimpleTransfersPerSecond", "NaN")
        }, warnings);
        Check(warnings.Count == 5 && bad.FilterLossChance == 0.5 && bad.MaxCraftsPerRequest == 999 && bad.JournalLines == 2000
            && bad.SimpleTransfersPerSecond == 2,
            "Unknown, out-of-range, fractional-integer and unparsable entries warn and keep defaults.");

        warnings.Clear();
        var twice = RscSettings.Parse(new[] { E("JournalLines", "500"), E("JournalLines", "600") }, warnings);
        Check(twice.JournalLines == 600 && warnings.Count == 1, "A repeated setting warns; the last one wins.");
        Check(RscSettings.Parse(null, warnings).MaxCraftsPerRequest == 999, "No file means defaults.");
        Check(defaults.Values().Count() == RscSettings.Names.Count() && defaults.Values().First().Key == "SimpleTransfersPerSecond",
            "Every setting is listed with its value.");

        var saved = RscSettings.Current;
        try
        {
            RscSettings.Current = s;
            Check(TransferLimits.For(ControllerTier.Simple) == 4, "Transfer limits follow the current settings.");
        }
        finally
        {
            RscSettings.Current = saved;
        }
    }

    private static void JournalRing()
    {
        var budget = new TransferBudget(2);
        budget.Charge(10.0, 3);
        Check(budget.Remaining(10.5) == -1 && !budget.TryConsume(10.5) && budget.Remaining(11.0) == 2,
            "A charge past the rate makes later transfers wait for the window (craft inputs, PROG-04).");

        var j = new Journal(3);
        for (int i = 1; i <= 5; i++)
            j.Add("line " + i);
        Check(j.Count == 3 && j.Last(10).SequenceEqual(new[] { "line 3", "line 4", "line 5" }),
            "The journal keeps only the newest lines, oldest first.");
        Check(j.Last(1).Single() == "line 5" && j.Last(5, "LINE 4").Single() == "line 4", "Last n with a filter.");
        Check(j.Unwritten == 3, "Unwritten lines are bounded too.");
        Check(j.TakeUnwritten().Count == 3 && j.Unwritten == 0 && j.Count == 3, "Flushing empties only the unwritten list.");
        string line = Journal.Format(new DateTime(2026, 10, 24, 1, 2, 3, DateTimeKind.Utc), "craft", "a\nb");
        Check(line == "2026-10-24T01:02:03.000Z craft a b", "Lines carry UTC time and kind, on one line.");
        Reject<ArgumentOutOfRangeException>(() => new Journal(0), "A journal needs room.");
    }
}
