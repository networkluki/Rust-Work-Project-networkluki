using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using System;

namespace Oxide.Plugins
{
    [Info("Clock", "networkluki", "1.0.0")]
    [Description("Shows in-game time and real server time")]

    public class Clock : RustPlugin
    {

        [ChatCommand("clock")]
        private void ClockCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            // Rust in-game time
            float rustTime = TOD_Sky.Instance.Cycle.Hour;

            int hours = Mathf.FloorToInt(rustTime);
            int minutes = Mathf.FloorToInt((rustTime - hours) * 60);

            string gameTime = $"{hours:00}:{minutes:00}";

            // Real server time
            DateTime now = DateTime.Now;
            string realTime = now.ToString("HH:mm:ss");

            SendReply(player,
                $"<color=#00ffff>Game Time:</color> {gameTime}\n" +
                $"<color=#ffd479>Real Time:</color> {realTime}"
            );
        }
    }
}