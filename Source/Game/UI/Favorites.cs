using System;
using System.Collections.Generic;
using System.IO;

namespace RefinedStorageControl.Game.UI
{
    // UI-02 favorites: a per-player list of item names kept on that player's own
    // machine (<user data>/RefinedStorageControl/favorites.txt, one name per
    // line), shared across worlds. A favorite covers every variant of the item.
    internal static class Favorites
    {
        private static HashSet<string> _names;

        private static string FilePath =>
            Path.Combine(Path.Combine(GameIO.GetUserGameDataDir(), "RefinedStorageControl"), "favorites.txt");

        public static HashSet<string> Names
        {
            get
            {
                if (_names == null)
                    Load();
                return _names;
            }
        }

        public static bool Toggle(string itemName)
        {
            bool added = Names.Add(itemName);
            if (!added)
                Names.Remove(itemName);
            Save();
            return added;
        }

        private static void Load()
        {
            _names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                        if (line.Trim().Length > 0)
                            _names.Add(line.Trim());
            }
            catch (Exception e)
            {
                RscLog.Warning("favorites could not be read: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = new List<string>(_names);
                lines.Sort(StringComparer.Ordinal);
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception e)
            {
                RscLog.Warning("favorites could not be saved: " + e.Message);
            }
        }
    }
}
