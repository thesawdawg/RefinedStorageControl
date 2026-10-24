using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using RefinedStorageControl.Config;

namespace RefinedStorageControl.Game
{
    // Reads <mod folder>/Settings.xml once at startup:
    //   <settings><setting name="SimpleTransfersPerSecond" value="2" /> ...</settings>
    // A missing or broken file means defaults; every problem is logged.
    internal static class SettingsFile
    {
        public const string FileName = "Settings.xml";

        public static void Load(string modPath)
        {
            var warnings = new List<string>();
            var entries = new List<KeyValuePair<string, string>>();
            string path = modPath == null ? null : Path.Combine(modPath, FileName);
            try
            {
                if (path == null || !File.Exists(path))
                    warnings.Add(FileName + " not found; using defaults");
                else
                {
                    var doc = new XmlDocument();
                    doc.Load(path);
                    foreach (XmlNode node in doc.SelectNodes("/settings/setting"))
                    {
                        string name = node.Attributes?["name"]?.Value;
                        string value = node.Attributes?["value"]?.Value;
                        if (string.IsNullOrEmpty(name) || value == null)
                            warnings.Add("a <setting> needs name and value attributes");
                        else
                            entries.Add(new KeyValuePair<string, string>(name, value));
                    }
                }
            }
            catch (Exception e)
            {
                warnings.Add(FileName + " unreadable (" + e.Message + "); using defaults");
                entries.Clear();
            }
            RscSettings.Current = RscSettings.Parse(entries, warnings);
            foreach (string w in warnings)
                RscLog.Warning("settings: " + w);
        }
    }
}
