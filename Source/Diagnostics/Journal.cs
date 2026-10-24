using System;
using System.Collections.Generic;

namespace RefinedStorageControl.Diagnostics
{
    // SAVE-03: the last N journal lines in memory, oldest dropped first, plus
    // the lines not yet written to the journal file.
    public sealed class Journal
    {
        private readonly Queue<string> _recent = new Queue<string>();
        private readonly List<string> _unwritten = new List<string>();

        public Journal(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public int Capacity { get; }
        public int Count => _recent.Count;
        public int Unwritten => _unwritten.Count;

        // One line: "<time> <kind> <message>", newlines flattened so a line
        // is always one entry.
        public static string Format(DateTime utc, string kind, string message) =>
            utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            + " " + (kind ?? "-") + " " + (message ?? "").Replace('\r', ' ').Replace('\n', ' ');

        public void Add(string line)
        {
            _recent.Enqueue(line);
            while (_recent.Count > Capacity)
                _recent.Dequeue();
            _unwritten.Add(line);
            // A file that cannot be written must not grow memory without end.
            if (_unwritten.Count > Capacity)
                _unwritten.RemoveRange(0, _unwritten.Count - Capacity);
        }

        // The last `n` lines, oldest first, optionally only those containing `filter`.
        public List<string> Last(int n, string filter = null)
        {
            var all = new List<string>(_recent);
            var result = new List<string>();
            for (int i = all.Count - 1; i >= 0 && result.Count < n; i--)
                if (string.IsNullOrEmpty(filter) || all[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(all[i]);
            result.Reverse();
            return result;
        }

        public List<string> TakeUnwritten()
        {
            var lines = new List<string>(_unwritten);
            _unwritten.Clear();
            return lines;
        }

        public void Clear()
        {
            _recent.Clear();
            _unwritten.Clear();
        }
    }
}
