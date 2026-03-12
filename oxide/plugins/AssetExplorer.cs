using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("AssetExplorer", "NetworkLuki", "1.2.0")]
    [Description("Exports all Rust assets and prefabs into categorized files")]

    public class AssetExplorer : RustPlugin
    {
        private string baseDir;

        void Init()
        {
            baseDir = Path.Combine(Interface.Oxide.LogDirectory, "assets");
        }

        void OnServerInitialized()
        {
            DumpAssets();
        }

        [ConsoleCommand("asset.scan")]
        private void CmdScan(ConsoleSystem.Arg arg)
        {
            DumpAssets();
            Puts("Asset scan finished.");
        }

        private void DumpAssets()
        {
            try
            {
                if (!Directory.Exists(baseDir))
                    Directory.CreateDirectory(baseDir);

                var all = new List<string>();
                var deployables = new List<string>();
                var buildings = new List<string>();
                var weapons = new List<string>();
                var vehicles = new List<string>();
                var npc = new List<string>();
                var items = new List<string>();

                foreach (var pooled in GameManifest.Current.pooledStrings)
                {
                    string path = pooled.str;

                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (!path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    all.Add(path);

                    string lower = path.ToLowerInvariant();

                    if (lower.Contains("/deployable/"))
                        deployables.Add(path);

                    if (lower.Contains("/building/"))
                        buildings.Add(path);

                    if (lower.Contains("/weapons/"))
                        weapons.Add(path);

                    if (lower.Contains("/vehicles/"))
                        vehicles.Add(path);

                    if (lower.Contains("/npc/") || lower.Contains("/ai/"))
                        npc.Add(path);

                    if (lower.Contains("/items/"))
                        items.Add(path);
                }

                WriteFile("all_assets.txt", all);
                WriteFile("deployables.txt", deployables);
                WriteFile("buildings.txt", buildings);
                WriteFile("weapons.txt", weapons);
                WriteFile("vehicles.txt", vehicles);
                WriteFile("npc.txt", npc);
                WriteFile("items.txt", items);

                Puts($"Asset scan completed. Total assets: {all.Count}");
            }
            catch (Exception ex)
            {
                PrintError($"Asset scan failed: {ex}");
            }
        }

        private void WriteFile(string name, List<string> data)
        {
            string path = Path.Combine(baseDir, name);

            var builder = new StringBuilder();

            foreach (var entry in data.OrderBy(x => x))
                builder.AppendLine(entry);

            File.WriteAllText(path, builder.ToString());
        }
    }
}