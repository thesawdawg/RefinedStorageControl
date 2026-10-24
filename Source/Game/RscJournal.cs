using System;
using System.IO;
using RefinedStorageControl.Config;
using RefinedStorageControl.Diagnostics;

namespace RefinedStorageControl.Game
{
    // SAVE-03: a bounded record of everything that moved or changed hands
    // (transfers, disks, crafting, destruction, admin commands). Kept in
    // memory for `rsc journal` and appended on the host to
    // <save>/RefinedStorageControl/journal.log, which rotates to journal.log.1
    // at JournalFileKb. Read-only: nothing replays it, so it can never
    // duplicate a committed transfer.
    internal static class RscJournal
    {
        private const string FileName = "journal.log";
        private const float FlushSeconds = 5f;

        private static Journal _journal = new Journal(RscSettings.Current.JournalLines);
        private static string _path;
        private static float _nextFlush;
        private static bool _writeFailed;

        public static void Add(string kind, string message) =>
            _journal.Add(Journal.Format(DateTime.UtcNow, kind, message));

        public static System.Collections.Generic.List<string> Last(int n, string filter) => _journal.Last(n, filter);

        // At game start on the host; clients keep only the in-memory copy.
        public static void Open()
        {
            _journal = new Journal(RscSettings.Current.JournalLines);
            _writeFailed = false;
            _path = ConnectionManager.Instance.IsServer ? Persistence.RscSaveFile.PathFor(FileName) : null;
            Add("session", "game started; settings " + string.Join(", ", Settings()));
        }

        public static void Close()
        {
            Flush();
            _path = null;
        }

        public static void Tick()
        {
            if (UnityEngine.Time.time < _nextFlush)
                return;
            _nextFlush = UnityEngine.Time.time + FlushSeconds;
            Flush();
        }

        public static void Flush()
        {
            if (_path == null || _journal.Unwritten == 0)
                return;
            var lines = _journal.TakeUnwritten();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > RscSettings.Current.JournalFileKb * 1024L)
                {
                    string old = _path + ".1";
                    if (File.Exists(old))
                        File.Delete(old);
                    File.Move(_path, old);
                }
                File.AppendAllLines(_path, lines);
                _writeFailed = false;
            }
            catch (Exception e)
            {
                if (!_writeFailed)
                    RscLog.Warning("journal not written: " + e.Message);
                _writeFailed = true;
            }
        }

        private static System.Collections.Generic.IEnumerable<string> Settings()
        {
            foreach (var pair in RscSettings.Current.Values())
                yield return pair.Key + "=" + pair.Value;
        }
    }
}
