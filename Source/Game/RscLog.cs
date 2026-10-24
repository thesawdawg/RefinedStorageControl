namespace RefinedStorageControl.Game
{
    internal static class RscLog
    {
        private const string Prefix = "[RSC] ";

        public static void Out(string message) => Log.Out(Prefix + message);

        public static void Warning(string message) => Log.Warning(Prefix + message);

        public static void Error(string message) => Log.Error(Prefix + message);
    }
}
