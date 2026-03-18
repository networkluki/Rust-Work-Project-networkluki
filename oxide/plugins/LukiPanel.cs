using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("LukiPanel", "networkluki", "4.0.0")]
    [Description("Advanced Builder Panel")]

    public class LukiPanel : RustPlugin
    {
        const string UI = "LukiBuilderUI";

        float radius;

        class ConfigData
        {
            public float Radius = 40f;
        }

        ConfigData config;

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        void Init()
        {
            config = Config.ReadObject<ConfigData>();
            radius = config.Radius;

            permission.RegisterPermission("lukipanel.use", this);
        }

        void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI);
        }

        void CreateUI(BasePlayer player)
        {
            DestroyUI(player);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.06 0.06 0.06 0.95" },
                RectTransform = { AnchorMin = "0.4 0.2", AnchorMax = "0.6 0.8" },
                CursorEnabled = true
            }, "Overlay", UI);

            AddButton(container, "UPGRADE WOOD", "lukipanel.wood", "0.1 0.78", "0.9 0.9");
            AddButton(container, "UPGRADE STONE", "lukipanel.stone", "0.1 0.64", "0.9 0.76");
            AddButton(container, "UPGRADE METAL", "lukipanel.metal", "0.1 0.50", "0.9 0.62");
            AddButton(container, "UPGRADE HQM", "lukipanel.hqm", "0.1 0.36", "0.9 0.48");

            AddButton(container, "REPAIR BASE", "lukipanel.repair", "0.1 0.22", "0.9 0.34");
            AddButton(container, "DELETE BASE", "lukipanel.delete", "0.1 0.10", "0.9 0.20");

            container.Add(new CuiButton
            {
                Button = { Close = UI, Color = "0.5 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.65 0.08" },
                Text = { Text = "CLOSE", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, UI);

            CuiHelper.AddUi(player, container);
        }

        void AddButton(CuiElementContainer container, string text, string cmd, string min, string max)
        {
            container.Add(new CuiButton
            {
                Button = { Command = cmd, Color = "0.2 0.2 0.2 1" },
                RectTransform = { AnchorMin = min, AnchorMax = max },
                Text = { Text = text, FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, UI);
        }

        [ChatCommand("lukipanel")]
        void OpenPanel(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, "lukipanel.use"))
            {
                player.ChatMessage("No permission");
                return;
            }

            CreateUI(player);
        }

        BasePlayer GetPlayer(ConsoleSystem.Arg arg)
        {
            return arg.Player() ?? arg.Connection?.player as BasePlayer;
        }

        bool HasTCAuth(BasePlayer player, BuildingBlock block)
        {
            var tc = block.GetBuildingPrivilege();
            if (tc == null) return false;

            return tc.IsAuthed(player);
        }

        void Upgrade(BasePlayer player, BuildingGrade.Enum grade)
        {
            List<BaseEntity> entities = new List<BaseEntity>();

            Vis.Entities(player.transform.position, radius, entities);

            foreach (var ent in entities)
            {
                var block = ent as BuildingBlock;
                if (block == null) continue;

                if (!HasTCAuth(player, block)) continue;

                block.ChangeGradeAndSkin(grade, 0, true);
                block.SetHealthToMax();
                block.SendNetworkUpdate();
            }
        }

        void Repair(BasePlayer player)
        {
            List<BaseEntity> entities = new List<BaseEntity>();

            Vis.Entities(player.transform.position, radius, entities);

            foreach (var ent in entities)
            {
                var block = ent as BuildingBlock;
                if (block == null) continue;

                if (!HasTCAuth(player, block)) continue;

                block.SetHealthToMax();
                block.SendNetworkUpdate();
            }
        }

        void Delete(BasePlayer player)
        {
            List<BaseEntity> entities = new List<BaseEntity>();

            Vis.Entities(player.transform.position, radius, entities);

            foreach (var ent in entities)
            {
                var block = ent as BuildingBlock;
                if (block == null) continue;

                if (!HasTCAuth(player, block)) continue;

                block.Kill();
            }
        }

        [ConsoleCommand("lukipanel.wood")]
        void Wood(ConsoleSystem.Arg arg)
        {
            var player = GetPlayer(arg);
            if (player == null) return;

            Upgrade(player, BuildingGrade.Enum.Wood);
        }

        [ConsoleCommand("lukipanel.stone")]
        void Stone(ConsoleSystem.Arg arg)
        {
            var player = GetPlayer(arg);
            if (player == null) return;

            Upgrade(player, BuildingGrade.Enum.Stone);
        }

        [ConsoleCommand("lukipanel.metal")]
        void Metal(ConsoleSystem.Arg arg)
        {
            var player = GetPlayer(arg);
            if (player == null) return;

            Upgrade(player, BuildingGrade.Enum.Metal);
        }

        [ConsoleCommand("lukipanel.hqm")]
        void HQM(ConsoleSystem.Arg arg)
        {
            var player = GetPlayer(arg);
            if (player == null) return;

            Upgrade(player, BuildingGrade.Enum.TopTier);
        }

        [ConsoleCommand("lukipanel.repair")]
        void RepairCmd(ConsoleSystem.Arg arg)
        {
            var player = GetPlayer(arg);
            if (player == null) return;

            Repair(player);
        }

        [ConsoleCommand("lukipanel.delete")]
        void DeleteCmd(ConsoleSystem.Arg arg)
        {
            var player = GetPlayer(arg);
            if (player == null) return;

            Delete(player);
        }
    }
}