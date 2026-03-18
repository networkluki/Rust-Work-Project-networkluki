using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("DamageDebugger", "NetworkLuki", "1.0.4")]
    [Description("Logs incoming damage events for troubleshooting traps, turrets, mines, NPCs, and player damage.")]
    public class DamageDebugger : RustPlugin
    {
        private const string PermissionUse = "damagedebugger.use";

        private bool _globalDebugEnabled;
        private readonly HashSet<ulong> _watchVictims = new HashSet<ulong>();
        private readonly HashSet<ulong> _watchAttackers = new HashSet<ulong>();

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
        }

        [ChatCommand("dd")]
        private void CommandDamageDebug(BasePlayer player, string command, string[] args)
        {
            if (player != null && !permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                Reply(player, "You do not have permission to use this command.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                SendHelp(player);
                return;
            }

            string sub = args[0].ToLowerInvariant();

            switch (sub)
            {
                case "on":
                    _globalDebugEnabled = true;
                    Reply(player, "DamageDebugger global logging: ON");
                    return;

                case "off":
                    _globalDebugEnabled = false;
                    _watchVictims.Clear();
                    _watchAttackers.Clear();
                    Reply(player, "DamageDebugger global logging: OFF and watch lists cleared.");
                    return;

                case "status":
                    Reply(player, $"Global: {_globalDebugEnabled} | WatchVictims: {_watchVictims.Count} | WatchAttackers: {_watchAttackers.Count}");
                    return;

                case "watchvictim":
                    HandleWatchPlayer(player, args, _watchVictims, "victim");
                    return;

                case "watchattacker":
                    HandleWatchPlayer(player, args, _watchAttackers, "attacker");
                    return;

                case "unwatchvictim":
                    HandleUnwatchPlayer(player, args, _watchVictims, "victim");
                    return;

                case "unwatchattacker":
                    HandleUnwatchPlayer(player, args, _watchAttackers, "attacker");
                    return;

                case "clear":
                    _watchVictims.Clear();
                    _watchAttackers.Clear();
                    Reply(player, "Watch lists cleared.");
                    return;

                default:
                    SendHelp(player);
                    return;
            }
        }

        private void SendHelp(BasePlayer player)
        {
            Reply(player, "DamageDebugger commands:");
            Reply(player, "/dd on - Enable global damage logging");
            Reply(player, "/dd off - Disable global logging and clear watch lists");
            Reply(player, "/dd status - Show debugger status");
            Reply(player, "/dd watchvictim <name|id> - Only log damage where this player is the victim");
            Reply(player, "/dd watchattacker <name|id> - Only log damage where this player is the attacker");
            Reply(player, "/dd unwatchvictim <name|id> - Remove victim watch");
            Reply(player, "/dd unwatchattacker <name|id> - Remove attacker watch");
            Reply(player, "/dd clear - Clear all watched players");
        }

        private void HandleWatchPlayer(BasePlayer caller, string[] args, HashSet<ulong> set, string kind)
        {
            if (args.Length < 2)
            {
                Reply(caller, $"Usage: /dd watch{kind} <name|id>");
                return;
            }

            BasePlayer target = FindPlayer(args[1]);
            if (target == null)
            {
                Reply(caller, $"Could not find player: {args[1]}");
                return;
            }

            set.Add(target.userID);
            Reply(caller, $"Now watching {kind}: {target.displayName} ({target.UserIDString})");
        }

        private void HandleUnwatchPlayer(BasePlayer caller, string[] args, HashSet<ulong> set, string kind)
        {
            if (args.Length < 2)
            {
                Reply(caller, $"Usage: /dd unwatch{kind} <name|id>");
                return;
            }

            BasePlayer target = FindPlayer(args[1]);
            if (target == null)
            {
                if (ulong.TryParse(args[1], out ulong rawId) && set.Remove(rawId))
                {
                    Reply(caller, $"Removed {kind} watch for ID {rawId}");
                    return;
                }

                Reply(caller, $"Could not find player: {args[1]}");
                return;
            }

            set.Remove(target.userID);
            Reply(caller, $"Stopped watching {kind}: {target.displayName} ({target.UserIDString})");
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
                return;

            BasePlayer victimPlayer = entity as BasePlayer;
            BasePlayer attackerPlayer = info.InitiatorPlayer;

            if (!ShouldLog(victimPlayer, attackerPlayer))
                return;

            string victimText = DescribeEntity(entity);
            string attackerText = DescribeAttacker(info);
            string weaponText = DescribeWeapon(info);
            string damageTypes = DescribeDamageTypes(info);
            float totalDamage = GetTotalDamage(info);
            string location = FormatVector(entity.transform.position);

            string line =
                $"[DamageDebugger] Victim={victimText} | Attacker={attackerText} | Weapon={weaponText} | " +
                $"Total={totalDamage.ToString("0.##", CultureInfo.InvariantCulture)} | Types={damageTypes} | " +
                $"HitBone={info.boneArea} | IsHeadshot={IsHeadshot(info)} | Position={location}";

            if (victimPlayer != null)
                line += $" | VictimSleeping={victimPlayer.IsSleeping()} | VictimConnected={victimPlayer.IsConnected}";

            Puts(line);
            LogToFile("DamageDebugger", line, this);
        }

        private bool ShouldLog(BasePlayer victimPlayer, BasePlayer attackerPlayer)
        {
            if (_globalDebugEnabled)
                return true;

            bool victimWatched = victimPlayer != null && _watchVictims.Contains(victimPlayer.userID);
            bool attackerWatched = attackerPlayer != null && _watchAttackers.Contains(attackerPlayer.userID);

            return victimWatched || attackerWatched;
        }

        private BasePlayer FindPlayer(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            BasePlayer exact = BasePlayer.FindAwakeOrSleeping(input);
            if (exact != null)
                return exact;

            foreach (BasePlayer player in BasePlayer.allPlayerList)
            {
                if (player == null || string.IsNullOrEmpty(player.displayName))
                    continue;

                if (player.displayName.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
                    return player;
            }

            return null;
        }

        private string DescribeEntity(BaseEntity entity)
        {
            if (entity == null)
                return "null";

            BasePlayer player = entity as BasePlayer;
            if (player != null)
                return $"Player:{player.displayName}({player.UserIDString})";

            return $"{entity.GetType().Name}:{entity.ShortPrefabName}:{(entity.net != null ? entity.net.ID.Value.ToString() : "no_netid")}";
        }

        private string DescribeAttacker(HitInfo info)
        {
            if (info == null)
                return "null";

            BasePlayer player = info.InitiatorPlayer;
            if (player != null)
                return $"Player:{player.displayName}({player.UserIDString})";

            BaseEntity initiator = info.Initiator;
            if (initiator != null)
                return $"{initiator.GetType().Name}:{initiator.ShortPrefabName}:{(initiator.net != null ? initiator.net.ID.Value.ToString() : "no_netid")}";

            return "null";
        }

        private string DescribeWeapon(HitInfo info)
        {
            if (info == null)
                return "null";

            if (info.Weapon != null)
                return $"{info.Weapon.GetType().Name}:{info.Weapon.ShortPrefabName}";

            if (info.WeaponPrefab != null)
                return $"{info.WeaponPrefab.GetType().Name}:{info.WeaponPrefab.ShortPrefabName}";

            if (info.Initiator != null)
                return $"{info.Initiator.GetType().Name}:{info.Initiator.ShortPrefabName}";

            return "null";
        }

        private float GetTotalDamage(HitInfo info)
        {
            if (info?.damageTypes == null)
                return 0f;

            return info.damageTypes.Total();
        }

        private string DescribeDamageTypes(HitInfo info)
        {
            if (info?.damageTypes == null)
                return "none";

            var parts = new List<string>();

            foreach (Rust.DamageType type in Enum.GetValues(typeof(Rust.DamageType)))
            {
                try
                {
                    float value = info.damageTypes.Get(type);
                    if (value > 0f)
                        parts.Add($"{type}={value.ToString("0.##", CultureInfo.InvariantCulture)}");
                }
                catch
                {
                }
            }

            return parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }

        private bool IsHeadshot(HitInfo info)
        {
            if (info == null)
                return false;

            return info.boneArea == HitArea.Head;
        }

        private string FormatVector(Vector3 position)
        {
            return $"({position.x:0.0}, {position.y:0.0}, {position.z:0.0})";
        }

        private void Reply(BasePlayer player, string message)
        {
            if (player == null)
            {
                Puts(message);
                return;
            }

            SendReply(player, message);
        }
    }
}