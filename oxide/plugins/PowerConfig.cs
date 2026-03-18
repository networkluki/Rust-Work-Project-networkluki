using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("PowerConfig", "networkluki", "1.1.0")]
    [Description("Configures Solar Panel and Test Generator power output via JSON config.")]
    public class PowerConfig : RustPlugin
    {
        // ─────────────────────────────────────────────
        //  Configuration
        // ─────────────────────────────────────────────

        private Configuration _config;

        private class Configuration
        {
            [JsonProperty("Solar Panel Power Output (watts)")]
            public int SolarPanelPower { get; set; } = 10000;

            [JsonProperty("Test Generator Power Output (watts)")]
            public int TestGeneratorPower { get; set; } = 100000;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) LoadDefaultConfig();
            }
            catch
            {
                PrintWarning("Invalid config — regenerating defaults.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(_config, true);

        // ─────────────────────────────────────────────
        //  Oxide Hooks
        // ─────────────────────────────────────────────

        private void OnServerInitialized()
        {
            ApplyToExistingEntities();
            Puts($"PowerConfig loaded — SolarPanel: {_config.SolarPanelPower}W | " +
                 $"TestGenerator: {_config.TestGeneratorPower}W");
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity is SolarPanel solar)
            {
                ApplySolarPower(solar);
                return;
            }
            if (entity is ElectricGenerator gen)
            {
                ApplyGeneratorPower(gen);
            }
        }

        // ─────────────────────────────────────────────
        //  Power Application
        // ─────────────────────────────────────────────

        private void ApplyToExistingEntities()
        {
            int solarCount = 0, genCount = 0;
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is SolarPanel sp)      { ApplySolarPower(sp);    solarCount++; }
                else if (entity is ElectricGenerator eg) { ApplyGeneratorPower(eg); genCount++;  }
            }
            Puts($"Patched {solarCount} solar panel(s) and {genCount} generator(s).");
        }

        /// <summary>
        /// maximalPowerOutput is the confirmed Int32 field on SolarPanel.
        /// It caps the sun-angle watt calculation — confirmed via reflection dump.
        /// </summary>
        private void ApplySolarPower(SolarPanel solar)
        {
            if (solar == null || solar.IsDestroyed) return;
            solar.maximalPowerOutput = _config.SolarPanelPower;
            solar.MarkDirtyForceUpdateOutputs();
            solar.SendNetworkUpdate();
        }

        /// <summary>
        /// electricAmount is the confirmed watt output field on ElectricGenerator.
        /// </summary>
        private void ApplyGeneratorPower(ElectricGenerator gen)
        {
            if (gen == null || gen.IsDestroyed) return;
            gen.electricAmount = _config.TestGeneratorPower;
            gen.MarkDirtyForceUpdateOutputs();
            gen.SendNetworkUpdate();
        }

        // ─────────────────────────────────────────────
        //  Chat Commands  (admin only)
        // ─────────────────────────────────────────────

        [ChatCommand("powerconfig")]
        private void CmdPowerConfig(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "<color=red>[PowerConfig]</color> Access denied.");
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player,
                    $"<color=yellow>[PowerConfig]</color>\n" +
                    $"  Solar Panel   : <color=cyan>{_config.SolarPanelPower}W</color>\n" +
                    $"  Test Generator: <color=cyan>{_config.TestGeneratorPower}W</color>\n\n" +
                    $"Usage: /powerconfig solar <watts>\n" +
                    $"       /powerconfig generator <watts>\n" +
                    $"       /powerconfig reload");
                return;
            }

            switch (args[0].ToLower())
            {
                case "solar":
                    if (args.Length < 2 || !int.TryParse(args[1], out int sw) || sw <= 0)
                    {
                        SendReply(player, "Usage: /powerconfig solar <positive integer>");
                        return;
                    }
                    _config.SolarPanelPower = sw;
                    SaveConfig();
                    ApplyToExistingEntities();
                    SendReply(player, $"<color=green>[PowerConfig]</color> Solar Panel set to {sw}W.");
                    Puts($"[PowerConfig] {player.displayName} set SolarPanel to {sw}W.");
                    break;

                case "generator":
                    if (args.Length < 2 || !int.TryParse(args[1], out int gw) || gw <= 0)
                    {
                        SendReply(player, "Usage: /powerconfig generator <positive integer>");
                        return;
                    }
                    _config.TestGeneratorPower = gw;
                    SaveConfig();
                    ApplyToExistingEntities();
                    SendReply(player, $"<color=green>[PowerConfig]</color> Test Generator set to {gw}W.");
                    Puts($"[PowerConfig] {player.displayName} set TestGenerator to {gw}W.");
                    break;

                case "reload":
                    LoadConfig();
                    ApplyToExistingEntities();
                    SendReply(player, "<color=green>[PowerConfig]</color> Config reloaded and applied.");
                    break;

                default:
                    SendReply(player, "Unknown subcommand. Use: solar | generator | reload");
                    break;
            }
        }

        // ─────────────────────────────────────────────
        //  Console Commands  (RCON / server console)
        // ─────────────────────────────────────────────

        [ConsoleCommand("powerconfig.set")]
        private void ConsolePowerConfigSet(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                arg.ReplyWith("Server console only.");
                return;
            }

            string type  = arg.GetString(0).ToLower();
            int    watts = arg.GetInt(1);

            if (watts <= 0)
            {
                arg.ReplyWith("Watts must be a positive integer.");
                return;
            }

            switch (type)
            {
                case "solar":
                    _config.SolarPanelPower = watts;
                    SaveConfig();
                    ApplyToExistingEntities();
                    arg.ReplyWith($"[PowerConfig] SolarPanel set to {watts}W.");
                    break;
                case "generator":
                    _config.TestGeneratorPower = watts;
                    SaveConfig();
                    ApplyToExistingEntities();
                    arg.ReplyWith($"[PowerConfig] TestGenerator set to {watts}W.");
                    break;
                default:
                    arg.ReplyWith("Usage: powerconfig.set <solar|generator> <watts>");
                    break;
            }
        }
    }
}
