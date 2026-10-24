using System;
using System.IO;

namespace RefinedStorageControl.Game.Persistence
{
    // Shared handling for RSC files in <save>/RefinedStorageControl/: the
    // power.dat/.bak pattern (write to .tmp, keep the previous good copy as .bak),
    // fall back to .bak on a bad read, and set unreadable files aside with an
    // .unreadable-<time> suffix instead of ever overwriting them.
    internal static class RscSaveFile
    {
        private const string FolderName = "RefinedStorageControl";

        public static string PathFor(string fileName) =>
            Path.Combine(Path.Combine(GameIO.GetSaveGameDir(), FolderName), fileName);

        // True with a value when the file or its .bak loads. False when neither
        // exists (a new world) or neither is readable (files set aside, logged).
        public static bool TryLoad<T>(string path, Func<BinaryReader, T> read, string label, out T value)
        {
            if (TryRead(path, read, label, out value) || TryRead(path + ".bak", read, label, out value))
                return true;
            if (File.Exists(path) || File.Exists(path + ".bak"))
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                foreach (var p in new[] { path, path + ".bak" })
                    if (File.Exists(p))
                        File.Move(p, p + ".unreadable-" + stamp);
                RscLog.Error(label + " unreadable; starting fresh. Old files kept with suffix .unreadable-" + stamp);
            }
            return false;
        }

        public static bool Write(string path, Action<BinaryWriter> write, string label)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                using (var writer = new BinaryWriter(File.Create(tmp)))
                    write(writer);
                if (File.Exists(path))
                    File.Copy(path, path + ".bak", overwrite: true);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                RscLog.Error(label + " save failed: " + e);
                return false;
            }
        }

        private static bool TryRead<T>(string path, Func<BinaryReader, T> read, string label, out T value)
        {
            value = default;
            if (!File.Exists(path))
                return false;
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(path)))
                    value = read(reader);
                return true;
            }
            catch (Exception e)
            {
                RscLog.Warning(label + " read failed for " + path + ": " + e.Message);
                return false;
            }
        }
    }
}
