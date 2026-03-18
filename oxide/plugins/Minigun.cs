using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Minigun", "networkluki", "2.0.0")]
    [Description("Minigun reload system with progress bar, sound effects, and movement cancellation")]
    public class Minigun : RustPlugin
    {
        // ─────────────────────────────────────────────────────────────
        // Configuration
        // ─────────────────────────────────────────────────────────────

        private class PluginConfig
        {
            public string PermissionToUse    { get; set; } = "minigun.use";
            public int    MaxAmmo            { get; set; } = 300;
            public float  ReloadTime         { get; set; } = 10f;
            public bool   AllowMovement      { get; set; } = false;
            public string AmmoType           { get; set; } = "ammo.rifle";

            // Sound effect prefab paths — must be valid entries in Rust's StringPool.
            // Use `oxide.log` + a StringPool dump plugin to find valid paths for your server version.
            // Set to empty string "" to disable a sound entirely.
            // These defaults use verified generic weapon sounds present in the base game.
            public string SoundReloadStart   { get; set; } = "assets/prefabs/weapons/bolt rifle/effects/bolt_cycle.prefab";
            public string SoundReloadFinish  { get; set; } = "assets/prefabs/weapons/bolt rifle/effects/rechamber_round.prefab";
        }

        private PluginConfig _cfg;

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _cfg = Config.ReadObject<PluginConfig>();
                if (_cfg == null) throw new System.Exception("null config");
            }
            catch
            {
                PrintWarning("Config invalid or missing — regenerating defaults.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            _cfg = new PluginConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_cfg);

        // ─────────────────────────────────────────────────────────────
        // Constants & State
        // ─────────────────────────────────────────────────────────────

        // UI element name — unique to avoid collisions with other plugins.
        private const string UI_ROOT        = "MinigunReloadUI";
        private const string UI_FILL        = "MinigunReloadUI_fill";

        // Velocity below which the player is considered stationary (m/s).
        private const float  MOVE_THRESHOLD = 0.5f;

        // How frequently (seconds) the progress bar and movement check tick.
        private const float  TICK_INTERVAL  = 0.1f;

        // Completion timers, keyed by SteamID.
        private readonly Dictionary<ulong, Timer>           _reloadTimers   = new Dictionary<ulong, Timer>();
        // Per-tick timers for progress update + movement/weapon-switch guard.
        private readonly Dictionary<ulong, Timer>           _tickTimers     = new Dictionary<ulong, Timer>();
        // The weapon instance that initiated the reload (to detect weapon switch).
        private readonly Dictionary<ulong, BaseProjectile>  _weapons        = new Dictionary<ulong, BaseProjectile>();

        // ─────────────────────────────────────────────────────────────
        // Oxide Hooks
        // ─────────────────────────────────────────────────────────────

        private void Init()
        {
            permission.RegisterPermission(_cfg.PermissionToUse, this);
        }

        private void Unload()
        {
            // Cancel every active reload cleanly so no ghost UI remains.
            foreach (var id in new List<ulong>(_reloadTimers.Keys))
            {
                var player = BasePlayer.FindByID(id);
                CancelReload(player, false);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
            => CancelReload(player, false);

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
            => CancelReload(player, false);

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;
            if (!input.WasJustPressed(BUTTON.RELOAD)) return;
            if (!permission.UserHasPermission(player.UserIDString, _cfg.PermissionToUse)) return;

            var weapon = player.GetActiveItem()?.GetHeldEntity() as BaseProjectile;
            if (weapon == null) return;

            // Only intercept for the minigun.
            if (!weapon.ShortPrefabName.Contains("minigun")) return;

            // Prevent double-triggering.
            if (_reloadTimers.ContainsKey(player.userID)) return;

            if (weapon.primaryMagazine.contents >= _cfg.MaxAmmo)
            {
                player.ChatMessage("<color=#ff6600>Minigun is already fully loaded.</color>");
                return;
            }

            StartReload(player, weapon);
        }

        // ─────────────────────────────────────────────────────────────
        // Reload Logic
        // ─────────────────────────────────────────────────────────────

        private void StartReload(BasePlayer player, BaseProjectile weapon)
        {
            _weapons[player.userID] = weapon;

            PlayEffect(player, _cfg.SoundReloadStart);
            ShowProgressBar(player, 0f);
            player.ChatMessage($"<color=#ffcc00>⟳ Reloading minigun…</color> ({_cfg.ReloadTime:F0}s)");

            float elapsed = 0f;

            // Tick: update progress bar, check movement, check weapon switch.
            var tick = timer.Every(TICK_INTERVAL, () =>
            {
                if (player == null || !player.IsConnected)
                {
                    CancelReload(player, false);
                    return;
                }

                // Weapon switch detection.
                var active = player.GetActiveItem()?.GetHeldEntity() as BaseProjectile;
                if (active != weapon)
                {
                    player.ChatMessage("<color=#ff4444>✗ Reload cancelled — weapon switched.</color>");
                    CancelReload(player, false);
                    return;
                }

                // Movement cancellation (if enabled in config).
                if (!_cfg.AllowMovement && player.estimatedVelocity.magnitude > MOVE_THRESHOLD)
                {
                    player.ChatMessage("<color=#ff4444>✗ Reload cancelled — you moved.</color>");
                    CancelReload(player, false);
                    return;
                }

                elapsed += TICK_INTERVAL;
                UpdateProgressBar(player, Mathf.Clamp01(elapsed / _cfg.ReloadTime));
            });

            _tickTimers[player.userID] = tick;

            // Completion timer.
            var reload = timer.Once(_cfg.ReloadTime, () => FinishReload(player, weapon));
            _reloadTimers[player.userID] = reload;
        }

        private void FinishReload(BasePlayer player, BaseProjectile weapon)
        {
            DestroyTimers(player.userID);
            DestroyProgressBar(player);
            _weapons.Remove(player.userID);

            if (player == null || !player.IsConnected || weapon == null) return;

            int needed = _cfg.MaxAmmo - weapon.primaryMagazine.contents;
            if (needed <= 0)
            {
                player.ChatMessage("<color=#ff6600>Minigun is already fully loaded.</color>");
                return;
            }

            var itemDef = ItemManager.FindItemDefinition(_cfg.AmmoType);
            if (itemDef == null)
            {
                PrintWarning($"[Minigun] Invalid AmmoType in config: \"{_cfg.AmmoType}\"");
                player.ChatMessage("<color=#ff4444>✗ Reload failed — invalid ammo type configured.</color>");
                return;
            }

            int taken = player.inventory.Take(null, itemDef.itemid, needed);

            if (taken == 0)
            {
                player.ChatMessage("<color=#ff4444>✗ No ammo in inventory — reload aborted.</color>");
                return;
            }

            weapon.primaryMagazine.contents += taken;
            weapon.SendNetworkUpdateImmediate();

            PlayEffect(player, _cfg.SoundReloadFinish);

            player.ChatMessage(
                $"<color=#00ff88>✓ Minigun reloaded! +{taken} rounds " +
                $"({weapon.primaryMagazine.contents}/{_cfg.MaxAmmo})</color>"
            );
        }

        /// <summary>
        /// Cancels an in-progress reload for the given player.
        /// Pass notify=true to send a chat message (used for non-automatic cancellations).
        /// </summary>
        private void CancelReload(BasePlayer player, bool notify)
        {
            if (player == null) return;

            DestroyTimers(player.userID);
            DestroyProgressBar(player);
            _weapons.Remove(player.userID);

            if (notify)
                player.ChatMessage("<color=#ff4444>✗ Reload cancelled.</color>");
        }

        private void DestroyTimers(ulong userId)
        {
            if (_tickTimers.TryGetValue(userId, out var tick))
            {
                tick?.Destroy();
                _tickTimers.Remove(userId);
            }
            if (_reloadTimers.TryGetValue(userId, out var rt))
            {
                rt?.Destroy();
                _reloadTimers.Remove(userId);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CUI — Progress Bar
        // ─────────────────────────────────────────────────────────────

        private void ShowProgressBar(BasePlayer player, float progress)
        {
            DestroyProgressBar(player); // Ensure no duplicate UI from a previous reload.

            var elements = new CuiElementContainer();

            // Outer container — bottom-centre of the screen.
            elements.Add(new CuiPanel
            {
                Image           = { Color = "0.08 0.08 0.08 0.88" },
                RectTransform   = { AnchorMin = "0.30 0.115", AnchorMax = "0.70 0.175" },
                CursorEnabled   = false
            }, "Hud", UI_ROOT);

            // Label — top half of the container.
            elements.Add(new CuiLabel
            {
                Text = { Text = "RELOADING MINIGUN", FontSize = 11,
                         Align = TextAnchor.MiddleCenter, Color = "0.95 0.85 0.2 1" },
                RectTransform = { AnchorMin = "0.01 0.52", AnchorMax = "0.99 0.98" }
            }, UI_ROOT);

            // Progress bar track — lower half of the container.
            elements.Add(new CuiPanel
            {
                Image           = { Color = "0.18 0.18 0.18 1" },
                RectTransform   = { AnchorMin = "0.01 0.08", AnchorMax = "0.99 0.50" }
            }, UI_ROOT, "MinigunReloadUI_track");

            // Progress fill — width driven by current progress (0–1).
            float right = 0.01f + 0.98f * Mathf.Clamp01(progress);
            elements.Add(new CuiPanel
            {
                Image           = { Color = "0.90 0.50 0.10 1" },
                RectTransform   = { AnchorMin = "0.01 0.08", AnchorMax = $"{right:F3} 0.50" }
            }, UI_ROOT, UI_FILL);

            CuiHelper.AddUi(player, elements);
        }

        /// <summary>
        /// Re-draws only the fill element to update the progress bar efficiently,
        /// avoiding a full destroy/recreate cycle every 100 ms.
        /// </summary>
        private void UpdateProgressBar(BasePlayer player, float progress)
        {
            CuiHelper.DestroyUi(player, UI_FILL);

            float right = 0.01f + 0.98f * Mathf.Clamp01(progress);
            var fill = new CuiElementContainer();

            fill.Add(new CuiPanel
            {
                Image           = { Color = "0.90 0.50 0.10 1" },
                RectTransform   = { AnchorMin = "0.01 0.08", AnchorMax = $"{right:F3} 0.50" }
            }, UI_ROOT, UI_FILL);

            CuiHelper.AddUi(player, fill);
        }

        private void DestroyProgressBar(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UI_ROOT);
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Plays a sound effect at the player's position, sent only to that client.
        /// Silently skips if the path is empty (allows disabling sounds via config).
        /// </summary>
        private void PlayEffect(BasePlayer player, string effectPath)
        {
            if (string.IsNullOrEmpty(effectPath) || player?.Connection == null) return;

            // StringPool.Get returns 0 for any path not registered in Rust's asset table.
            // Sending an unpooled path produces a warning and is silently dropped by the engine.
            if (StringPool.Get(effectPath) == 0)
            {
                PrintWarning($"[Minigun] Sound effect path is not pooled and will not play: \"{effectPath}\"");
                return;
            }

            var effect = new Effect(effectPath, player.transform.position, Vector3.zero);
            EffectNetwork.Send(effect, player.Connection);
        }
    }
}
