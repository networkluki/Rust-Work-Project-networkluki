using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace Oxide.Plugins
{
    [Info("CoordManager", "networkluki", "3.0.0")]
    [Description("Advanced coordinate manager with GUI, teleport, copy and logging")]

    public class CoordManager : RustPlugin
    {
        const string permUse = "coordmanager.use";

        Dictionary<ulong, Timer> activeGUIs = new();
        Dictionary<string, Vector3> savedCoords = new();

        void Init()
        {
            permission.RegisterPermission(permUse, this);
            LoadData();
        }

        void Unload()
        {
            foreach (var t in activeGUIs.Values)
                t.Destroy();
        }

        [ChatCommand("coords")]
        void CmdCoords(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
            {
                player.ChatMessage("No permission.");
                return;
            }

            StartGUI(player);
        }

        void StartGUI(BasePlayer player)
        {
            StopGUI(player);

            activeGUIs[player.userID] = timer.Every(0.5f, () =>
            {
                if (player == null || !player.IsConnected)
                {
                    StopGUI(player);
                    return;
                }

                DrawGUI(player);
            });
        }

        void StopGUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "CoordGUI");

            if (activeGUIs.ContainsKey(player.userID))
            {
                activeGUIs[player.userID].Destroy();
                activeGUIs.Remove(player.userID);
            }
        }

        void DrawGUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "CoordGUI");

            Vector3 pos = player.transform.position;

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.07 0.07 0.92" },
                RectTransform = { AnchorMin = "0.02 0.72", AnchorMax = "0.27 0.92" },
                CursorEnabled = true
            }, "Overlay", "CoordGUI");

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = "COORD MANAGER",
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = { AnchorMin = "0 0.82", AnchorMax = "1 0.95" }
            }, "CoordGUI");

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"X {pos.x:F2}   Y {pos.y:F2}   Z {pos.z:F2}",
                    FontSize = 15,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = { AnchorMin = "0 0.60", AnchorMax = "1 0.80" }
            }, "CoordGUI");

            container.Add(new CuiButton
            {
                Button = { Command = $"coord.save {pos.x} {pos.y} {pos.z}", Color = "0.18 0.65 0.18 1" },
                RectTransform = { AnchorMin = "0.05 0.45", AnchorMax = "0.30 0.55" },
                Text = { Text = "SAVE", FontSize = 12 }
            }, "CoordGUI");

            container.Add(new CuiButton
            {
                Button = { Command = $"coord.copy {pos.x} {pos.y} {pos.z}", Color = "0.25 0.45 0.9 1" },
                RectTransform = { AnchorMin = "0.35 0.45", AnchorMax = "0.65 0.55" },
                Text = { Text = "COPY", FontSize = 12 }
            }, "CoordGUI");

            container.Add(new CuiButton
            {
                Button = { Command = "coord.close", Color = "0.8 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0.70 0.45", AnchorMax = "0.95 0.55" },
                Text = { Text = "CLOSE", FontSize = 12 }
            }, "CoordGUI");

            float offset = 0.30f;

            foreach (var entry in savedCoords)
            {
                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = entry.Key,
                        FontSize = 12,
                        Align = TextAnchor.MiddleLeft
                    },
                    RectTransform = { AnchorMin = $"0.05 {offset}", AnchorMax = $"0.50 {offset + 0.07}" }
                }, "CoordGUI");

                container.Add(new CuiButton
                {
                    Button = { Command = $"coord.tp {entry.Key}", Color = "0.25 0.45 0.9 1" },
                    RectTransform = { AnchorMin = $"0.55 {offset}", AnchorMax = $"0.72 {offset + 0.07}" },
                    Text = { Text = "TP", FontSize = 11 }
                }, "CoordGUI");

                container.Add(new CuiButton
                {
                    Button = { Command = $"coord.delete {entry.Key}", Color = "0.8 0.3 0.3 1" },
                    RectTransform = { AnchorMin = $"0.75 {offset}", AnchorMax = $"0.95 {offset + 0.07}" },
                    Text = { Text = "DEL", FontSize = 11 }
                }, "CoordGUI");

                offset -= 0.08f;
            }

            CuiHelper.AddUi(player, container);
        }

        void LogCoords(BasePlayer player, string action, Vector3 pos)
        {
            string log =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {player.displayName} ({player.UserIDString})\n" +
                $"ACTION: {action}\n" +
                $"X: {pos.x:F2} Y: {pos.y:F2} Z: {pos.z:F2}\n";

            LogToFile("coords", log, this);
        }

        [ConsoleCommand("coord.close")]
        void CmdClose(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            StopGUI(player);
        }

        [ConsoleCommand("coord.save")]
        void CmdSave(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            float x = float.Parse(arg.Args[0]);
            float y = float.Parse(arg.Args[1]);
            float z = float.Parse(arg.Args[2]);

            Vector3 pos = new Vector3(x, y, z);

            string name = $"pos_{savedCoords.Count + 1}";
            savedCoords[name] = pos;

            SaveData();

            LogCoords(player, "SAVE", pos);

            player.ChatMessage($"Saved position {name}");
        }

        [ConsoleCommand("coord.copy")]
        void CmdCopy(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            float x = float.Parse(arg.Args[0]);
            float y = float.Parse(arg.Args[1]);
            float z = float.Parse(arg.Args[2]);

            Vector3 pos = new Vector3(x, y, z);

            LogCoords(player, "COPY", pos);

            player.ChatMessage($"Coords: {x:F2} {y:F2} {z:F2}");
        }

        [ConsoleCommand("coord.tp")]
        void CmdTP(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            string name = arg.Args[0];

            if (!savedCoords.ContainsKey(name))
                return;

            Vector3 pos = savedCoords[name];

            player.Teleport(pos);

            LogCoords(player, $"TELEPORT {name}", pos);
        }

        [ConsoleCommand("coord.delete")]
        void CmdDelete(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            string name = arg.Args[0];

            if (!savedCoords.ContainsKey(name))
                return;

            savedCoords.Remove(name);

            SaveData();

            player.ChatMessage($"Deleted {name}");
        }

        void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("CoordManager_Data", savedCoords);
        }

        void LoadData()
        {
            try
            {
                savedCoords = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, Vector3>>("CoordManager_Data");
            }
            catch
            {
                savedCoords = new Dictionary<string, Vector3>();
            }
        }
    }
}