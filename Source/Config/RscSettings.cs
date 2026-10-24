using System;
using System.Collections.Generic;
using System.Globalization;

namespace RefinedStorageControl.Config
{
    // Balance and technical values players may change (MOD-03), read from the
    // mod's Settings.xml at startup. The host's values govern every rule it
    // enforces; a client's copy only shapes its own window. A missing,
    // unknown or out-of-range entry keeps the default and adds a warning.
    public sealed class RscSettings
    {
        private sealed class Field
        {
            public string Name;
            public double Min;
            public double Max;
            public bool Integer;
            public Action<RscSettings, double> Set;
            public Func<RscSettings, double> Get;
        }

        // PROG-04 / PROG-06 (F12; the advanced cap is finalized by the P4 benchmark).
        public int SimpleTransfersPerSecond { get; private set; } = 2;
        public int AdvancedTransfersPerSecond { get; private set; } = 20;

        // DISK-04 / DISK-05 (F01).
        public double DiskSurvivalChance { get; private set; } = 0.5;
        public double FilterLossChance { get; private set; } = 0.5;

        // CRAFT-04 countdown and the largest single request.
        public double CraftCountdownSeconds { get; private set; } = 5.0;
        public int MaxCraftsPerRequest { get; private set; } = 999;

        // How long items RSC drops on the ground stay there.
        public double DropLifetimeSeconds { get; private set; } = 3600;

        // SAVE-03 journal: lines kept in memory and the file size before rotating.
        public int JournalLines { get; private set; } = 2000;
        public int JournalFileKb { get; private set; } = 1024;

        public static RscSettings Current { get; set; } = new RscSettings();

        private static readonly Field[] Fields =
        {
            Int("SimpleTransfersPerSecond", 1, 100, (s, v) => s.SimpleTransfersPerSecond = (int)v, s => s.SimpleTransfersPerSecond),
            Int("AdvancedTransfersPerSecond", 1, 1000, (s, v) => s.AdvancedTransfersPerSecond = (int)v, s => s.AdvancedTransfersPerSecond),
            Real("DiskSurvivalChance", 0, 1, (s, v) => s.DiskSurvivalChance = v, s => s.DiskSurvivalChance),
            Real("FilterLossChance", 0, 1, (s, v) => s.FilterLossChance = v, s => s.FilterLossChance),
            Real("CraftCountdownSeconds", 1, 60, (s, v) => s.CraftCountdownSeconds = v, s => s.CraftCountdownSeconds),
            // Native queue entries count crafts in a 16-bit field.
            Int("MaxCraftsPerRequest", 1, 9999, (s, v) => s.MaxCraftsPerRequest = (int)v, s => s.MaxCraftsPerRequest),
            Real("DropLifetimeSeconds", 60, 86400, (s, v) => s.DropLifetimeSeconds = v, s => s.DropLifetimeSeconds),
            Int("JournalLines", 100, 100000, (s, v) => s.JournalLines = (int)v, s => s.JournalLines),
            Int("JournalFileKb", 64, 65536, (s, v) => s.JournalFileKb = (int)v, s => s.JournalFileKb)
        };

        public static IEnumerable<string> Names
        {
            get
            {
                foreach (var f in Fields)
                    yield return f.Name;
            }
        }

        public static RscSettings Parse(IEnumerable<KeyValuePair<string, string>> entries, List<string> warnings)
        {
            var settings = new RscSettings();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
                return settings;
            foreach (var entry in entries)
            {
                var field = Array.Find(Fields, f => string.Equals(f.Name, entry.Key, StringComparison.OrdinalIgnoreCase));
                if (field == null)
                {
                    warnings?.Add("unknown setting '" + entry.Key + "' ignored");
                    continue;
                }
                if (!seen.Add(field.Name))
                    warnings?.Add("setting '" + field.Name + "' given twice; the last one wins");
                if (!double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    || double.IsNaN(value) || double.IsInfinity(value)
                    || (field.Integer && value != Math.Floor(value)))
                {
                    warnings?.Add("setting '" + field.Name + "' has an invalid value '" + entry.Value + "'; using "
                        + Format(field.Get(settings)));
                    continue;
                }
                if (value < field.Min || value > field.Max)
                {
                    warnings?.Add("setting '" + field.Name + "' = " + entry.Value + " is outside " + Format(field.Min) + ".."
                        + Format(field.Max) + "; using " + Format(field.Get(settings)));
                    continue;
                }
                field.Set(settings, value);
            }
            return settings;
        }

        public IEnumerable<KeyValuePair<string, string>> Values()
        {
            foreach (var f in Fields)
                yield return new KeyValuePair<string, string>(f.Name, Format(f.Get(this)));
        }

        private static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static Field Int(string name, int min, int max, Action<RscSettings, double> set, Func<RscSettings, double> get) =>
            new Field { Name = name, Min = min, Max = max, Integer = true, Set = set, Get = get };

        private static Field Real(string name, double min, double max, Action<RscSettings, double> set, Func<RscSettings, double> get) =>
            new Field { Name = name, Min = min, Max = max, Set = set, Get = get };
    }
}
