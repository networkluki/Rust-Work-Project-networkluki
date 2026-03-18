using HarmonyLib;
using Oxide.Core;
using System;
using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RealDigitalClock", "networkluki", "3.1.0")]
    [Description("Digital Clocks show real-world time. Sky permanently locked to 13:00.")]
    public class RealDigitalClock : RustPlugin
    {
        private const float DAYTIME_HOUR  = 13f;
        private const float LOCK_INTERVAL = 5f;

        internal static float RealHour = DAYTIME_HOUR;

        private Timer    _lockTimer;
        private Harmony  _harmony;
        private TOD_Time _timeComponent;

        // -----------------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------------

        void OnServerInitialized()
        {
            if (TOD_Sky.Instance == null) { PrintError("TOD_Sky.Instance is null."); return; }

            _timeComponent = TOD_Sky.Instance.Components.Time;
            if (_timeComponent == null) { PrintError("TOD_Time component not found."); return; }

            _timeComponent.ProgressTime = false;

            _harmony = new Harmony("com.networkluki.realdigitalclock");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Puts("Harmony patches applied.");

            UpdateRealHour();
            LockSky();
            _lockTimer = timer.Every(LOCK_INTERVAL, LockSky);
        }

        void Unload()
        {
            _lockTimer?.Destroy();
            _lockTimer = null;
            _harmony?.UnpatchAll("com.networkluki.realdigitalclock");
            _harmony = null;
            if (_timeComponent != null)
                _timeComponent.ProgressTime = true;
            Puts("Unloaded.");
        }

        // -----------------------------------------------------------------------
        // Lock sky visually to 13:00 AND keep ConVar in sync
        // TOD_Sky.Cycle.Hour controls sun position/lighting
        // ConVar.Env.time must match it to stay at daytime between clock saves
        // -----------------------------------------------------------------------
        private void LockSky()
        {
            UpdateRealHour();
            if (TOD_Sky.Instance == null) return;
            TOD_Sky.Instance.Cycle.Hour = DAYTIME_HOUR;
            ConVar.Env.time             = DAYTIME_HOUR;
        }

        internal static void UpdateRealHour()
        {
            DateTime now = DateTime.Now;
            RealHour = now.Hour + (now.Minute / 60f);
        }

        // -----------------------------------------------------------------------
        // Harmony: DigitalClock.Save(SaveInfo info)
        //
        // IMPORTANT: Must match the exact signature — Save takes a SaveInfo param.
        // Prefix:  swap ConVar.Env.time to real time so the serialized value is correct
        // Postfix: restore ConVar.Env.time to 13:00 so lighting stays at daytime
        // -----------------------------------------------------------------------

        [HarmonyPatch(typeof(DigitalClock), "Save")]
        private static class Patch_DigitalClock_Save
        {
            // SaveInfo is the parameter type — declare as object to avoid assembly ref issues
            static void Prefix(object info)
            {
                UpdateRealHour();
                ConVar.Env.time = RealHour;
            }

            static void Postfix(object info)
            {
                ConVar.Env.time             = DAYTIME_HOUR;
                if (TOD_Sky.Instance != null)
                    TOD_Sky.Instance.Cycle.Hour = DAYTIME_HOUR;
            }
        }
    }
}
