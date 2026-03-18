using Oxide.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("HomeSystem", "networkluki", "1.2.0")]
    [Description("Home system with limit counter and teleport countdown")]

    public class HomeSystem : RustPlugin
    {
        private const int MaxHomes = 5;
        private const float RaidBlockTime = 300f;
        private const float TeleportDelay = 15f;

        private Dictionary<ulong, Dictionary<string, Vector3>> homes = new();
        private Dictionary<ulong, float> raidBlocked = new();

        void Init()
        {
            LoadData();
        }

        void Unload()
        {
            SaveData();
        }

        [ChatCommand("home")]
        void HomeCommand(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp(player);
                return;
            }

            switch (args[0].ToLower())
            {
                case "help":
                    ShowHelp(player);
                    return;

                case "list":
                    ListHomes(player);
                    return;

                case "add":
                    if (args.Length < 2)
                    {
                        SendReply(player, "Usage: /home add name");
                        return;
                    }
                    AddHome(player, args[1]);
                    return;

                case "remove":
                    if (args.Length < 2)
                    {
                        SendReply(player, "Usage: /home remove name");
                        return;
                    }
                    RemoveHome(player, args[1]);
                    return;

                default:
                    TeleportHome(player, args[0]);
                    return;
            }
        }

        void ShowHelp(BasePlayer player)
        {
            SendReply(player, "<color=#00FFFF>Home Commands</color>");
            SendReply(player, "/home add name");
            SendReply(player, "/home remove name");
            SendReply(player, "/home list");
            SendReply(player, "/home name");
        }

        void ListHomes(BasePlayer player)
        {
            var id = player.userID;

            if (!homes.ContainsKey(id))
                homes[id] = new Dictionary<string, Vector3>();

            int count = homes[id].Count;

            SendReply(player, $"<color=#00FFFF>Your Homes ({count}/{MaxHomes})</color>");

            if (count == 0)
            {
                SendReply(player, "No homes saved.");
                return;
            }

            foreach (var home in homes[id])
            {
                SendReply(player, $"- {home.Key}");
            }
        }

        void AddHome(BasePlayer player, string name)
        {
            var id = player.userID;

            if (!homes.ContainsKey(id))
                homes[id] = new Dictionary<string, Vector3>();

            if (homes[id].Count >= MaxHomes)
            {
                SendReply(player, $"Home limit reached ({MaxHomes}/{MaxHomes})");
                return;
            }

            homes[id][name] = player.transform.position;

            SaveData();

            SendReply(player, $"Home '{name}' saved ({homes[id].Count}/{MaxHomes})");
        }

        void RemoveHome(BasePlayer player, string name)
        {
            var id = player.userID;

            if (!homes.ContainsKey(id) || !homes[id].ContainsKey(name))
            {
                SendReply(player, "Home not found.");
                return;
            }

            homes[id].Remove(name);

            SaveData();

            SendReply(player, $"Home '{name}' removed ({homes[id].Count}/{MaxHomes})");
        }

        void TeleportHome(BasePlayer player, string name)
        {
            var id = player.userID;

            if (IsRaidBlocked(player))
            {
                SendReply(player, "You are raid blocked!");
                return;
            }

            if (!homes.ContainsKey(id) || !homes[id].ContainsKey(name))
            {
                SendReply(player, "Home not found.");
                return;
            }

            Vector3 pos = homes[id][name];

            SendReply(player, $"Teleporting to '{name}' in {TeleportDelay} seconds...");

            timer.Once(TeleportDelay, () =>
            {
                if (player == null || !player.IsConnected) return;

                player.Teleport(pos);
                SendReply(player, $"Teleported to {name}");
            });
        }

        bool IsRaidBlocked(BasePlayer player)
        {
            if (!raidBlocked.ContainsKey(player.userID))
                return false;

            if (raidBlocked[player.userID] < Time.realtimeSinceStartup)
            {
                raidBlocked.Remove(player.userID);
                return false;
            }

            return true;
        }

        void BlockPlayer(BasePlayer player)
        {
            raidBlocked[player.userID] = Time.realtimeSinceStartup + RaidBlockTime;
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.InitiatorPlayer == null) return;

            BasePlayer attacker = info.InitiatorPlayer;

            BlockPlayer(attacker);

            if (entity is BasePlayer victim)
                BlockPlayer(victim);
        }

        void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("HomeSystem", homes);
        }

        void LoadData()
        {
            try
            {
                homes = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, Dictionary<string, Vector3>>>("HomeSystem");
            }
            catch
            {
                homes = new Dictionary<ulong, Dictionary<string, Vector3>>();
            }
        }
    }
}