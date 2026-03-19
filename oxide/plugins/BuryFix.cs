// BuryFix.cs — Oxide/uMod plugin for Rust (v3.2 — Harmony finalizer)
//
// v3.0-3.1: Prefix checks on item/item.info/item.uid — patches applied,
//           but NullRef still thrown INSIDE BuriedItem.Create after checks pass.
//           The null is something else: a singleton, manager, or world-state.
//
// v3.2: Adds Harmony FINALIZER patches that catch NullReferenceException
//       thrown from within the patched methods and suppress them silently.
//       The bury operation simply doesn't happen — no crash, no log spam.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BuryFix", "networkluki", "3.2.0")]
    [Description("Suppresses NullReferenceException in BuriedItem.Create via Harmony finalizer")]
    public class BuryFix : RustPlugin
    {
        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            public bool LogPrevention { get; set; } = true;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch
            {
                PrintWarning("Config corrupt — loading defaults.");
                _config = new Configuration();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Static State

        private static bool _logEnabled = true;
        private static Action<string> _logAction;

        // Prefix catches (item-level nulls)
        private static int _prefixBlocked = 0;
        // Finalizer catches (internal nulls inside Create/Register)
        private static int _finalizerCaught = 0;

        #endregion

        #region Harmony Setup

        private Harmony _harmony;
        private const string HarmonyId = "com.buryfix.rust";

        private void OnServerInitialized()
        {
            _logEnabled = _config.LogPrevention;
            _logAction = msg => Puts(msg);

            try
            {
                _harmony = new Harmony(HarmonyId);
                PatchCreate();
                PatchRegister();
                Puts("[BuryFix] Harmony patching complete (v3.2 — prefix + finalizer).");
            }
            catch (Exception ex)
            {
                PrintError($"[BuryFix] Fatal error during patching: {ex.Message}");
                PrintError(ex.StackTrace);
            }
        }

        private void Unload()
        {
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Puts($"[BuryFix] Patches removed. " +
                     $"Prefix blocked: {_prefixBlocked}, Finalizer caught: {_finalizerCaught}");
            }
            catch (Exception ex)
            {
                PrintError($"[BuryFix] Error removing patches: {ex.Message}");
            }
            _logAction = null;
        }

        #endregion

        #region Patch: BuriedItem.Create

        private void PatchCreate()
        {
            MethodInfo target = typeof(BuriedItem).GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(Item), typeof(Vector3), typeof(long) },
                null
            );

            if (target == null)
            {
                PrintWarning("[BuryFix] Could not find BuriedItem.Create");
                return;
            }

            // Prefix: skip if item is obviously invalid
            MethodInfo prefix = typeof(BuryFix).GetMethod(
                nameof(Prefix_Create),
                BindingFlags.Static | BindingFlags.NonPublic
            );

            // Finalizer: catch any NullRef that escapes the prefix checks
            MethodInfo finalizer = typeof(BuryFix).GetMethod(
                nameof(Finalizer_Create),
                BindingFlags.Static | BindingFlags.NonPublic
            );

            _harmony.Patch(target,
                prefix: new HarmonyMethod(prefix),
                finalizer: new HarmonyMethod(finalizer)
            );

            Puts("[BuryFix] Patched: BuriedItem.Create (prefix + finalizer)");
        }

        #endregion

        #region Patch: BuriedItems.Register (instance method)

        private void PatchRegister()
        {
            Type buriedItemsType = typeof(BuriedItems);

            // Find instance Register(Item, Vector3)
            MethodInfo target = null;
            MethodInfo[] allMethods = buriedItemsType.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic
            );

            foreach (MethodInfo m in allMethods)
            {
                if (m.Name != "Register") continue;

                ParameterInfo[] ps = m.GetParameters();
                bool hasItem = false;
                bool hasVector = false;

                foreach (ParameterInfo p in ps)
                {
                    if (p.ParameterType == typeof(Item)) hasItem = true;
                    if (p.ParameterType == typeof(Vector3)) hasVector = true;
                }

                if (hasItem && hasVector)
                {
                    target = m;
                    break;
                }
            }

            if (target == null)
            {
                PrintWarning("[BuryFix] Could not find BuriedItems.Register");
                return;
            }

            MethodInfo prefix = typeof(BuryFix).GetMethod(
                nameof(Prefix_Register),
                BindingFlags.Static | BindingFlags.NonPublic
            );

            MethodInfo finalizer = typeof(BuryFix).GetMethod(
                nameof(Finalizer_Register),
                BindingFlags.Static | BindingFlags.NonPublic
            );

            _harmony.Patch(target,
                prefix: new HarmonyMethod(prefix),
                finalizer: new HarmonyMethod(finalizer)
            );

            string flags = target.IsStatic ? "static" : "instance";
            Puts($"[BuryFix] Patched: BuriedItems.Register ({flags}, prefix + finalizer)");
        }

        #endregion

        #region Prefix Methods (first line of defense)

        private static bool Prefix_Create(Item item, Vector3 worldPosition, long expiryTime)
        {
            if (item == null || item.info == null || item.uid.Value == 0)
            {
                _prefixBlocked++;
                LogPrevention($"[BuryFix] PREFIX blocked Create at {worldPosition} (#{_prefixBlocked})");
                return false;
            }
            return true;
        }

        private static bool Prefix_Register(Item item)
        {
            if (item == null || item.info == null)
            {
                _prefixBlocked++;
                LogPrevention($"[BuryFix] PREFIX blocked Register (#{_prefixBlocked})");
                return false;
            }
            return true;
        }

        #endregion

        #region Finalizer Methods (catches exceptions the prefix missed)

        /// <summary>
        /// Harmony finalizer for BuriedItem.Create.
        /// If the original method throws NullReferenceException,
        /// we suppress it by returning null (= no exception to propagate).
        /// </summary>
        private static Exception Finalizer_Create(Exception __exception)
        {
            if (__exception == null)
                return null; // No exception — normal flow

            if (__exception is NullReferenceException)
            {
                _finalizerCaught++;
                LogPrevention($"[BuryFix] FINALIZER caught NullRef in Create (#{_finalizerCaught})");
                return null; // Suppress the exception
            }

            // Not a NullRef — let it propagate normally
            return __exception;
        }

        /// <summary>
        /// Harmony finalizer for BuriedItems.Register.
        /// Same logic: suppress NullReferenceException, let others through.
        /// </summary>
        private static Exception Finalizer_Register(Exception __exception)
        {
            if (__exception == null)
                return null;

            if (__exception is NullReferenceException)
            {
                _finalizerCaught++;
                LogPrevention($"[BuryFix] FINALIZER caught NullRef in Register (#{_finalizerCaught})");
                return null;
            }

            return __exception;
        }

        #endregion

        #region Logging

        private static void LogPrevention(string message)
        {
            if (!_logEnabled) return;
            _logAction?.Invoke(message);
        }

        #endregion

        #region Commands

        [ChatCommand("buryfix")]
        private void CmdBuryFix(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("<color=#ff4444>BuryFix: Admin only.</color>");
                return;
            }

            player.ChatMessage(
                $"<color=#44ff44>═══ BuryFix v3.2 (Harmony) ═══</color>\n" +
                $"Prefix blocked (bad items):   <color=#ffaa00>{_prefixBlocked}</color>\n" +
                $"Finalizer caught (internal):  <color=#ffaa00>{_finalizerCaught}</color>\n" +
                $"Patches: <color=#44ff44>Active (prefix + finalizer)</color>");
        }

        [ConsoleCommand("buryfix.stats")]
        private void CcmdStats(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            Puts($"[BuryFix] Prefix: {_prefixBlocked} | Finalizer: {_finalizerCaught}");
        }

        [ConsoleCommand("buryfix.reset")]
        private void CcmdReset(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            _prefixBlocked = 0;
            _finalizerCaught = 0;
            Puts("[BuryFix] Counters reset.");
        }

        #endregion
    }
}
