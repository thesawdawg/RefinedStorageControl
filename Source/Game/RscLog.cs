namespace RefinedStorageControl.Game
{
    internal static class RscLog
    {
        private const string Prefix = "[RSC] ";

        public static void Out(string message) => Log.Out(Prefix + message);

        // Game log plus the journal (SAVE-03): anything that moved items, disks
        // or jobs, or changed who owns what.
        public static void Event(string kind, string message)
        {
            Log.Out(Prefix + message);
            RscJournal.Add(kind, message);
        }

        public static void Warning(string message) => Log.Warning(Prefix + message);

        public static void Error(string message) => Log.Error(Prefix + message);
    }
}
