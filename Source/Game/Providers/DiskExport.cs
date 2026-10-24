using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Persistence;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Providers
{
    // SAVE-03 media export: a readable, tab-separated record of one disk
    // (owner, access list, where it is installed, every item with its exact
    // key in base64) written to <save>/RefinedStorageControl/export/. It only
    // reads the disk; nothing imports it back automatically, so it can never
    // duplicate items. It is for owners and admins to see or restore by hand.
    internal static class DiskExport
    {
        public static string Write(DiskStore store, DiskRecord disk)
        {
            string dir = Path.Combine(Path.GetDirectoryName(RscSaveFile.PathFor("x")), "export");
            Directory.CreateDirectory(dir);
            string id = disk.Id.ToString("N");
            string path = Path.Combine(dir, "disk-" + id.Substring(0, 8) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");

            var lines = new List<string>
            {
                "# Refined Storage Control disk export (read-only record; not imported automatically)",
                "disk\t" + id,
                "world\t" + store.WorldId.ToString("N"),
                "tier\t" + disk.Tier,
                "used\t" + disk.Used + "\t" + "capacity\t" + disk.Capacity,
                "owner\t" + CabinetService.NameOf(disk.Acl.OwnerId) + "\t" + disk.Acl.OwnerId
            };
            foreach (var entry in disk.Acl.Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                lines.Add("access\t" + CabinetService.NameOf(entry.Key) + "\t" + entry.Key + "\t" + entry.Value);
            var where = store.FindCabinet(disk.Id);
            if (where.HasValue)
            {
                PositionKey.Unpack(where.Value.cabinet, out int x, out int y, out int z);
                lines.Add("installed\t" + x + " " + y + " " + z + "\tslot " + (where.Value.slot + 1));
            }
            else
                lines.Add("installed\tno");
            lines.Add("exported\t" + DateTime.UtcNow.ToString("u"));
            lines.Add("");
            lines.Add("count\titem\tname\tquality\tuses\tkey");

            var rows = new List<(string item, string text)>();
            foreach (var pair in disk.Items)
            {
                var sample = ItemKeys.ToItemValue(pair.Key);
                var cls = sample.ItemClass;
                string item = cls?.GetItemName() ?? "(unknown type " + sample.type + ")";
                rows.Add((item, pair.Value + "\t" + item + "\t" + (cls?.GetLocalizedItemName() ?? "") + "\t"
                    + (sample.HasQuality ? sample.Quality.ToString() : "") + "\t"
                    + (sample.HasQuality ? sample.UseTimes.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "")
                    + "\t" + Convert.ToBase64String(pair.Key.ToArray())));
            }
            lines.AddRange(rows.OrderBy(r => r.item, StringComparer.Ordinal).ThenBy(r => r.text, StringComparer.Ordinal)
                .Select(r => r.text));
            File.WriteAllLines(path, lines);
            return path;
        }
    }
}
