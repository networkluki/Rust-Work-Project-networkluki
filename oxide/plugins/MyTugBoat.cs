using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MyTugBoat", "networkluki", "1.0.3")]
    [Description("Allows players to spawn and manage TugBoats.")]

    public class MyTugBoat : RustPlugin
    {
        // ─── Permissions ─────────────────────────────────────────────────────────────
        private const string permUse   = "mytugboat.use";
        private const string permFetch = "mytugboat.fetch";
        private const string permVip   = "mytugboat.vip";

        // ─── State ───────────────────────────────────────────────────────────────────
        private Dictionary<ulong, BaseEntity> playerTugs    = new Dictionary<ulong, BaseEntity>();
        private Dictionary<ulong, DateTime>   spawnCooldown = new Dictionary<ulong, DateTime>();
        private Dictionary<ulong, DateTime>   fetchCooldown = new Dictionary<ulong, DateTime>();

        private ConfigData config;

        // ─── Config ──────────────────────────────────────────────────────────────────
        class ConfigData
        {
            public bool  InvincibleTug            = false;
            public bool  NoDecay                  = false;
            public int   FetchCooldownMinutes      = 10;
            public bool  Use24HourSpawnRestriction = true;
            public float TugHealth                 = 3000f;
            public float SpawnDistance             = 20f;
        }

        protected override void LoadDefaultConfig() { config = new ConfigData(); SaveConfig(); }
        protected override void LoadConfig()        { base.LoadConfig(); config = Config.ReadObject<ConfigData>(); }
        protected override void SaveConfig()        { Config.WriteObject(config, true); }

        void Init()
        {
            permission.RegisterPermission(permUse,   this);
            permission.RegisterPermission(permFetch, this);
            permission.RegisterPermission(permVip,   this);
        }

        // ─── /tugboat ────────────────────────────────────────────────────────────────
        [ChatCommand("tugboat")]
        void TugBoatCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
            {
                SendReply(player, "You don't have permission.");
                return;
            }

            if (args.Length > 0 && args[0].ToLower() == "fetch")
            {
                FetchBoat(player);
                return;
            }

            SpawnBoat(player);
        }

        // ─── Spawn ───────────────────────────────────────────────────────────────────
        void SpawnBoat(BasePlayer player)
        {
            ulong id = player.userID;

            if (playerTugs.ContainsKey(id) && playerTugs[id] != null && !playerTugs[id].IsDestroyed)
            {
                SendReply(player, "You already own a TugBoat.");
                return;
            }

            if (config.Use24HourSpawnRestriction && spawnCooldown.ContainsKey(id))
            {
                TimeSpan remaining = spawnCooldown[id] - DateTime.UtcNow;
                if (remaining.TotalSeconds > 0)
                {
                    SendReply(player, $"You must wait {remaining.Hours}h {remaining.Minutes}m before spawning another TugBoat.");
                    return;
                }
            }

            Vector3 position = player.transform.position + player.eyes.HeadForward() * config.SpawnDistance;

            BaseEntity tug = GameManager.server.CreateEntity(
                "assets/content/vehicles/boats/tugboat/tugboat.prefab", position);

            if (tug == null)
            {
                SendReply(player, "Failed to spawn TugBoat.");
                return;
            }

            tug.Spawn();

            // Set OwnerID so the player can place doors, locks, etc.
            tug.OwnerID = id;

            // Set health via BaseCombatEntity — InitializeHealth sets both current
            // and max so the engine does not clamp back to the prefab default.
            BaseCombatEntity tugCombat = tug as BaseCombatEntity;
            if (tugCombat != null)
            {
                tugCombat.InitializeHealth(config.TugHealth, config.TugHealth);
                tugCombat.SendNetworkUpdate();
            }

            playerTugs[id]    = tug;
            spawnCooldown[id] = DateTime.UtcNow.AddHours(24);

            SendReply(player, "Your TugBoat has been spawned.");
        }

        // Returns true if the given entity is a child (any depth) of the tugboat.
        // Used to determine whether a lock belongs to a player's own tugboat.
        private bool IsChildOfTug(BaseEntity child, BaseEntity tug)
        {
            BaseEntity current = child.GetParentEntity();
            while (current != null)
            {
                if (current == tug) return true;
                current = current.GetParentEntity();
            }
            return false;
        }

        // ─── Fetch ───────────────────────────────────────────────────────────────────
        void FetchBoat(BasePlayer player)
        {
            ulong id = player.userID;

            if (!permission.UserHasPermission(player.UserIDString, permFetch))
            {
                SendReply(player, "You don't have permission to fetch your TugBoat.");
                return;
            }

            if (!playerTugs.ContainsKey(id) || playerTugs[id] == null || playerTugs[id].IsDestroyed)
            {
                SendReply(player, "You don't own a TugBoat.");
                return;
            }

            if (fetchCooldown.ContainsKey(id))
            {
                TimeSpan remaining = fetchCooldown[id] - DateTime.UtcNow;
                if (remaining.TotalSeconds > 0)
                {
                    SendReply(player, $"Fetch cooldown: {remaining.Minutes} minutes remaining.");
                    return;
                }
            }

            Vector3 newPosition = player.transform.position + player.eyes.HeadForward() * 40f;

            playerTugs[id].transform.position = newPosition;
            playerTugs[id].SendNetworkUpdateImmediate();

            fetchCooldown[id] = DateTime.UtcNow.AddMinutes(config.FetchCooldownMinutes);

            SendReply(player, "Your TugBoat has been fetched.");
        }

        // ─── Allow owner to use locks on their own tugboat ──────────────────────────
        // Rust calls CanUseLockedEntity whenever a player tries to open/interact with
        // a locked entity (code lock, key lock). If the lock is parented to the
        // player's tugboat we return null (allow) — this bypasses the vehicle
        // privilege popup entirely without modifying the privilege entity itself.
        object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (player == null || baseLock == null) return null;

            ulong id = player.userID;
            if (!playerTugs.ContainsKey(id)) return null;

            BaseEntity tug = playerTugs[id];
            if (tug == null || tug.IsDestroyed) return null;

            if (IsChildOfTug(baseLock, tug))
                return null; // null = allow, do not show permission popup

            return null; // not our tug — let Rust decide
        }

        // ─── Tracker cleanup on entity death ─────────────────────────────────────────
        void OnEntityKill(BaseNetworkable entity)
        {
            BaseEntity baseEntity = entity as BaseEntity;
            if (baseEntity == null) return;

            ulong ownerKey = 0;
            foreach (var entry in playerTugs)
            {
                if (entry.Value == baseEntity) { ownerKey = entry.Key; break; }
            }

            if (ownerKey != 0)
                playerTugs.Remove(ownerKey);
        }

        // ─── Damage gate for InvincibleTug / NoDecay ─────────────────────────────────
        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!config.InvincibleTug && !config.NoDecay) return null;

            foreach (var tug in playerTugs.Values)
            {
                if (tug != entity) continue;

                if (config.InvincibleTug)
                {
                    info.damageTypes.ScaleAll(0f);
                    return true;
                }

                if (config.NoDecay && info.damageTypes.Has(Rust.DamageType.Decay))
                {
                    info.damageTypes.Scale(Rust.DamageType.Decay, 0f);
                    return true;
                }

                break;
            }

            return null;
        }
    }
}
