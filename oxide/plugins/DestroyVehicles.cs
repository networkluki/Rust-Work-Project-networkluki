using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("DestroyVehicles", "networkluki", "1.0.6")]
    [Description("Allows admins to destroy all vehicles within a radius using /dv <radius>.")]

    public class DestroyVehicles : RustPlugin
    {
        // ─── Permission ──────────────────────────────────────────────────────────────
        private const string permAdmin = "destroyvehicles.admin";

        // ─── Config ──────────────────────────────────────────────────────────────────
        private class ConfigData
        {
            public float MaxRadius = 500f;
        }

        private ConfigData config;

        protected override void LoadDefaultConfig() { config = new ConfigData(); SaveConfig(); }
        protected override void LoadConfig()        { base.LoadConfig(); config = Config.ReadObject<ConfigData>(); }
        protected override void SaveConfig()        { Config.WriteObject(config, true); }

        void Init()
        {
            permission.RegisterPermission(permAdmin, this);
        }

        // ─── Exact ShortPrefabName matches ───────────────────────────────────────────
        // IMPORTANT: Use exact names, NOT Contains/substrings.
        // Killing the root entity automatically destroys all children anyway.
        private static readonly HashSet<string> vehiclePrefabNames = new HashSet<string>
        {
            // ── Boats ────────────────────────────────────────────────────────────────
            "tugboat",
            "rowboat",
            "rhib",

            // ── Air (player-rideable) ────────────────────────────────────────────────
            "minicopter.entity",
            "scraptransporthelicopter",
            "attackhelicopter.entity",
            "hotairballoon",

            // ── Air (NPC / event) ────────────────────────────────────────────────────
            "patrolhelicopter",            // NPC patrol helicopter that attacks players
            "ch47scientists.entity",       // Chinook — drops crates & scientists

            // ── Land (player-driveable) ──────────────────────────────────────────────
            "bradleyapc",                  // Bradley APC
            "2module_car_spawned.entity",  // Modular car 2-module
            "3module_car_spawned.entity",  // Modular car 3-module
            "4module_car_spawned.entity",  // Modular car 4-module
            "sedantest.entity",            // Sedan
            "motorbike",                   // Motorbike
            "workcart",
            "workcart_aboveground",
            "magnetcrane.entity",
            "snowmobile",
            "tomahasnowmobile",            // Tomaha snowmobile variant

            // ── Water ────────────────────────────────────────────────────────────────
            "submarinesolo.entity",
            "submarineduo.entity",
            "kayak",

            // ── Animal ───────────────────────────────────────────────────────────────
            "horse.player",
        };

        // ─── /dv <radius> ────────────────────────────────────────────────────────────
        [ChatCommand("dv")]
        void DestroyVehiclesCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permAdmin))
            {
                SendReply(player, "<color=#ff4444>[DV]</color> Du har inte tillstånd att använda /dv.");
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player, "<color=#ffcc00>[DV]</color> Användning: /dv <radius>   Exempel: /dv 50");
                return;
            }

            float radius;
            if (!float.TryParse(args[0], out radius) || radius <= 0f)
            {
                SendReply(player, "<color=#ff4444>[DV]</color> Radius måste vara ett positivt tal.");
                return;
            }

            if (radius > config.MaxRadius)
            {
                SendReply(player, $"<color=#ffcc00>[DV]</color> Radius begränsad till max {config.MaxRadius} m.");
                radius = config.MaxRadius;
            }

            Vector3 origin   = player.transform.position;
            float   radiusSq = radius * radius;

            // Collect first — never Kill() while iterating serverEntities.
            List<BaseEntity> toKill = new List<BaseEntity>();

            foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = net as BaseEntity;
                if (entity == null || entity.IsDestroyed) continue;

                if ((entity.transform.position - origin).sqrMagnitude > radiusSq) continue;

                // Exact name match — avoids hitting child entities that share a prefix.
                if (vehiclePrefabNames.Contains(entity.ShortPrefabName))
                    toKill.Add(entity);
            }

            int killed = 0;
            foreach (BaseEntity entity in toKill)
            {
                if (!entity.IsDestroyed)
                {
                    entity.Kill();
                    killed++;
                }
            }

            SendReply(player, $"<color=#00ff88>[DV]</color> Förstörde <color=#ffffff>{killed}</color> fordon inom <color=#ffffff>{radius}</color> m.");

            Puts($"[DestroyVehicles] {player.displayName} ({player.UserIDString}) " +
                 $"raderade {killed} fordon inom {radius} m vid {origin}.");
        }

        // ─── /dvdebug <radius> — lists raw ShortPrefabName of nearby entities ───────
        // Use this if a vehicle still isn't deleted: run /dvdebug 20, find the exact
        // ShortPrefabName, and report it so it can be added to vehiclePrefabNames.
        [ChatCommand("dvdebug")]
        void DvDebugCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permAdmin)) return;

            float radius = 30f;
            if (args.Length > 0) float.TryParse(args[0], out radius);

            Vector3 origin   = player.transform.position;
            float   radiusSq = radius * radius;

            HashSet<string> found = new HashSet<string>();

            foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = net as BaseEntity;
                if (entity == null || entity.IsDestroyed) continue;
                if ((entity.transform.position - origin).sqrMagnitude > radiusSq) continue;
                if (!string.IsNullOrEmpty(entity.ShortPrefabName))
                    found.Add($"{entity.ShortPrefabName}  (OwnerID={entity.OwnerID})");
            }

            SendReply(player, $"Entities within {radius} m:");
            foreach (string s in found)
                SendReply(player, "  " + s);
        }
    }
}
