using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("LoadoutController", "networkluki", "1.0.1")]
    [Description("Admin UI to assign persistent per-player loadouts, given on respawn.")]
    public class LoadoutController : RustPlugin
    {
        private const string PermAdmin = "loadoutcontroller.admin";
        private const string UiRoot = "LoadoutControllerUI";

        private StoredData data;
        private ConfigData cfg;

        private readonly Dictionary<ulong, UiState> uiState = new Dictionary<ulong, UiState>();
        private readonly HashSet<ulong> givenThisLife = new HashSet<ulong>();

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);

            LoadConfigValues();
            LoadData();
            EnsureDefaultLoadoutExists();
        }

        private void OnServerInitialized()
        {
            EnsureDefaultLoadoutExists();
        }

        private void OnPlayerInit(BasePlayer player)
        {
            if (player == null || player.IsNpc || player.userID == 0) return;

            if (cfg.AutoAssignDefaultOnFirstJoin && !data.PlayerLoadout.ContainsKey(player.userID))
            {
                if (!string.IsNullOrEmpty(data.DefaultLoadoutId) && data.Loadouts.ContainsKey(data.DefaultLoadoutId))
                {
                    data.PlayerLoadout[player.userID] = data.DefaultLoadoutId;
                    SaveData();
                }
            }
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null || player.IsNpc || player.userID == 0) return;
            if (givenThisLife.Contains(player.userID)) return;

            Loadout loadout = GetPlayerLoadout(player.userID);
            if (loadout == null) return;

            float delay = cfg.GiveDelaySeconds < 0f ? 0f : cfg.GiveDelaySeconds;
            timer.Once(delay, () =>
            {
                if (player == null) return;
                if (!player.IsConnected) return;
                if (player.IsDead()) return;

                ApplyLoadout(player, loadout);
                givenThisLife.Add(player.userID);
            });
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null) return;
            if (givenThisLife.Contains(player.userID)) givenThisLife.Remove(player.userID);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            if (givenThisLife.Contains(player.userID)) givenThisLife.Remove(player.userID);
            DestroyUI(player);
        }

        #endregion

        #region Chat Command

        [ChatCommand("loadouts")]
        private void CmdLoadouts(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAllowed(player))
            {
                player.ChatMessage("No permission.");
                return;
            }

            if (args != null && args.Length > 0 && args[0].Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                DestroyUI(player);
                return;
            }

            OpenUI(player);
        }

        #endregion

        #region UI Console Commands

        [ConsoleCommand("loadoutui.close")]
        private void UiClose(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (!IsAdminAllowed(player)) return;
            DestroyUI(player);
        }

        [ConsoleCommand("loadoutui.selectplayer")]
        private void UiSelectPlayer(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            ulong uid;
            if (!ulong.TryParse(arg.GetString(0, "0"), out uid)) return;

            UiState s = GetState(admin.userID);
            s.SelectedPlayer = uid;
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.selectloadout")]
        private void UiSelectLoadout(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            string id = NormalizeId(arg.GetString(0, ""));
            if (string.IsNullOrEmpty(id)) return;

            UiState s = GetState(admin.userID);
            s.SelectedLoadoutId = id;
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.players.prev")]
        private void UiPlayersPrev(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            s.PlayerPage = Math.Max(0, s.PlayerPage - 1);
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.players.next")]
        private void UiPlayersNext(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            s.PlayerPage = s.PlayerPage + 1;
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.loadouts.prev")]
        private void UiLoadoutsPrev(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            s.LoadoutPage = Math.Max(0, s.LoadoutPage - 1);
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.loadouts.next")]
        private void UiLoadoutsNext(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            s.LoadoutPage = s.LoadoutPage + 1;
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.assign")]
        private void UiAssign(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            if (s.SelectedPlayer == 0 || string.IsNullOrEmpty(s.SelectedLoadoutId)) { OpenUI(admin); return; }
            if (!data.Loadouts.ContainsKey(s.SelectedLoadoutId)) { OpenUI(admin); return; }

            data.PlayerLoadout[s.SelectedPlayer] = s.SelectedLoadoutId;
            SaveData();
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.clear")]
        private void UiClear(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            if (s.SelectedPlayer == 0) { OpenUI(admin); return; }

            if (data.PlayerLoadout.ContainsKey(s.SelectedPlayer))
                data.PlayerLoadout.Remove(s.SelectedPlayer);

            SaveData();
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.setdefault")]
        private void UiSetDefault(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            if (string.IsNullOrEmpty(s.SelectedLoadoutId)) { OpenUI(admin); return; }

            if (data.Loadouts.ContainsKey(s.SelectedLoadoutId))
            {
                data.DefaultLoadoutId = s.SelectedLoadoutId;
                SaveData();
            }

            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.applynow")]
        private void UiApplyNow(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            if (s.SelectedPlayer == 0) { OpenUI(admin); return; }

            BasePlayer target = BasePlayer.FindByID(s.SelectedPlayer);
            if (target == null) target = BasePlayer.FindSleeping(s.SelectedPlayer);
            if (target == null) { OpenUI(admin); return; }

            Loadout loadout = GetPlayerLoadout(s.SelectedPlayer);
            if (loadout == null) { OpenUI(admin); return; }

            ApplyLoadout(target, loadout);
            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.saveauto")]
        private void UiSaveAuto(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            string id = "auto_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            Loadout loadout = CaptureFromPlayer(admin);

            data.Loadouts[id] = loadout;
            SaveData();

            UiState s = GetState(admin.userID);
            s.SelectedLoadoutId = id;

            OpenUI(admin);
        }

        [ConsoleCommand("loadoutui.delete")]
        private void UiDelete(ConsoleSystem.Arg arg)
        {
            BasePlayer admin = arg.Player();
            if (!IsAdminAllowed(admin)) return;

            UiState s = GetState(admin.userID);
            string id = NormalizeId(s.SelectedLoadoutId);
            if (string.IsNullOrEmpty(id)) { OpenUI(admin); return; }
            if (id == "default") { OpenUI(admin); return; }

            if (data.Loadouts.ContainsKey(id))
                data.Loadouts.Remove(id);

            // clear bindings
            List<ulong> toClear = new List<ulong>();
            foreach (var kv in data.PlayerLoadout)
                if (kv.Value == id) toClear.Add(kv.Key);

            for (int i = 0; i < toClear.Count; i++)
                data.PlayerLoadout.Remove(toClear[i]);

            if (data.DefaultLoadoutId == id)
                data.DefaultLoadoutId = "default";

            SaveData();

            s.SelectedLoadoutId = data.DefaultLoadoutId;
            OpenUI(admin);
        }

        #endregion

        #region UI Build

        private void OpenUI(BasePlayer admin)
        {
            DestroyUI(admin);

            UiState s = GetState(admin.userID);

            List<BasePlayer> players = BasePlayer.activePlayerList
                .Where(p => p != null && !p.IsNpc && p.userID != 0)
                .OrderBy(p => p.displayName)
                .ToList();

            List<string> loadoutIds = data.Loadouts.Keys.OrderBy(k => k).ToList();

            int rows = cfg.UiRows;
            if (rows < 6) rows = 6;
            if (rows > 20) rows = 20;

            int maxPlayerPages = Math.Max(1, (int)Math.Ceiling(players.Count / (double)rows));
            int maxLoadoutPages = Math.Max(1, (int)Math.Ceiling(loadoutIds.Count / (double)rows));

            s.PlayerPage = Mathf.Clamp(s.PlayerPage, 0, maxPlayerPages - 1);
            s.LoadoutPage = Mathf.Clamp(s.LoadoutPage, 0, maxLoadoutPages - 1);

            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.70" },
                RectTransform = { AnchorMin = "0.12 0.12", AnchorMax = "0.88 0.88" },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            container.Add(new CuiLabel
            {
                Text = { Text = "LOADOUT CONTROLLER", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 0.995" }
            }, UiRoot);

            container.Add(new CuiButton
            {
                Button = { Color = "0.8 0.2 0.2 0.9", Command = "loadoutui.close" },
                RectTransform = { AnchorMin = "0.95 0.93", AnchorMax = "0.99 0.985" },
                Text = { Text = "X", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiRoot);

            container.Add(new CuiLabel
            {
                Text = { Text = "PLAYERS", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.03 0.86", AnchorMax = "0.47 0.91" }
            }, UiRoot);

            container.Add(new CuiLabel
            {
                Text = { Text = "LOADOUTS", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.53 0.86", AnchorMax = "0.97 0.91" }
            }, UiRoot);

            container.Add(new CuiButton
            {
                Button = { Color = "0.2 0.2 0.2 0.8", Command = "loadoutui.players.prev" },
                RectTransform = { AnchorMin = "0.35 0.86", AnchorMax = "0.41 0.91" },
                Text = { Text = "<", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiRoot);

            container.Add(new CuiButton
            {
                Button = { Color = "0.2 0.2 0.2 0.8", Command = "loadoutui.players.next" },
                RectTransform = { AnchorMin = "0.42 0.86", AnchorMax = "0.48 0.91" },
                Text = { Text = ">", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiRoot);

            container.Add(new CuiButton
            {
                Button = { Color = "0.2 0.2 0.2 0.8", Command = "loadoutui.loadouts.prev" },
                RectTransform = { AnchorMin = "0.85 0.86", AnchorMax = "0.91 0.91" },
                Text = { Text = "<", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiRoot);

            container.Add(new CuiButton
            {
                Button = { Color = "0.2 0.2 0.2 0.8", Command = "loadoutui.loadouts.next" },
                RectTransform = { AnchorMin = "0.92 0.86", AnchorMax = "0.98 0.91" },
                Text = { Text = ">", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiRoot);

            float listTop = 0.84f;
            float listBottom = 0.26f;

            // players
            int pStart = s.PlayerPage * rows;
            int pEnd = Math.Min(players.Count, pStart + rows);

            for (int i = pStart; i < pEnd; i++)
            {
                BasePlayer p = players[i];

                float rowH = (listTop - listBottom) / rows;
                float yMax = listTop - ((i - pStart) * rowH);
                float yMin = yMax - rowH + 0.002f;

                bool selected = (s.SelectedPlayer == p.userID);
                string bg = selected ? "0.2 0.5 0.8 0.85" : "0.15 0.15 0.15 0.75";

                string assigned = data.PlayerLoadout.ContainsKey(p.userID) ? data.PlayerLoadout[p.userID] : "(default)";
                string label = p.displayName + " [" + p.userID + "]\n" + assigned;

                container.Add(new CuiButton
                {
                    Button = { Color = bg, Command = "loadoutui.selectplayer " + p.userID },
                    RectTransform = { AnchorMin = "0.03 " + yMin, AnchorMax = "0.47 " + yMax },
                    Text = { Text = label, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
                }, UiRoot);
            }

            // loadouts
            int lStart = s.LoadoutPage * rows;
            int lEnd = Math.Min(loadoutIds.Count, lStart + rows);

            for (int i = lStart; i < lEnd; i++)
            {
                string id = loadoutIds[i];

                float rowH = (listTop - listBottom) / rows;
                float yMax = listTop - ((i - lStart) * rowH);
                float yMin = yMax - rowH + 0.002f;

                bool selected = string.Equals(s.SelectedLoadoutId, id, StringComparison.OrdinalIgnoreCase);
                bool isDefault = string.Equals(data.DefaultLoadoutId, id, StringComparison.OrdinalIgnoreCase);

                string bg = selected ? "0.2 0.8 0.4 0.85" : "0.15 0.15 0.15 0.75";
                string suffix = isDefault ? " (DEFAULT)" : "";

                container.Add(new CuiButton
                {
                    Button = { Color = bg, Command = "loadoutui.selectloadout " + NormalizeId(id) },
                    RectTransform = { AnchorMin = "0.53 " + yMin, AnchorMax = "0.97 " + yMax },
                    Text = { Text = id + suffix, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
                }, UiRoot);
            }

            // status
            string selPlayerName = "(none)";
            if (s.SelectedPlayer != 0)
            {
                BasePlayer sp = BasePlayer.FindByID(s.SelectedPlayer);
                selPlayerName = sp != null ? sp.displayName : s.SelectedPlayer.ToString();
            }

            container.Add(new CuiLabel
            {
                Text = { Text = "Selected Player: " + selPlayerName + "\nSelected Loadout: " + (s.SelectedLoadoutId ?? "(none)") + "\nDefault: " + data.DefaultLoadoutId, FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.03 0.17", AnchorMax = "0.97 0.25" }
            }, UiRoot);

            AddBtn(container, "ASSIGN", "loadoutui.assign", "0.03 0.10", "0.20 0.16", "0.2 0.6 0.9 0.85");
            AddBtn(container, "CLEAR", "loadoutui.clear", "0.21 0.10", "0.38 0.16", "0.6 0.3 0.2 0.85");
            AddBtn(container, "SET DEFAULT", "loadoutui.setdefault", "0.39 0.10", "0.58 0.16", "0.3 0.7 0.3 0.85");
            AddBtn(container, "APPLY NOW", "loadoutui.applynow", "0.59 0.10", "0.78 0.16", "0.7 0.7 0.2 0.85");
            AddBtn(container, "SAVE NEW (FROM MY INV)", "loadoutui.saveauto", "0.03 0.03", "0.40 0.09", "0.2 0.2 0.2 0.85");
            AddBtn(container, "DELETE SELECTED", "loadoutui.delete", "0.41 0.03", "0.58 0.09", "0.8 0.2 0.2 0.85");
            AddBtn(container, "CLOSE", "loadoutui.close", "0.79 0.03", "0.97 0.09", "0.2 0.2 0.2 0.85");

            CuiHelper.AddUi(admin, container);
        }

        private void AddBtn(CuiElementContainer c, string text, string cmd, string aMin, string aMax, string color)
        {
            c.Add(new CuiButton
            {
                Button = { Color = color, Command = cmd },
                RectTransform = { AnchorMin = aMin, AnchorMax = aMax },
                Text = { Text = text, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiRoot);
        }

        private void DestroyUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UiRoot);
        }

        private UiState GetState(ulong adminId)
        {
            UiState s;
            if (!uiState.TryGetValue(adminId, out s))
            {
                s = new UiState();
                s.SelectedPlayer = 0;
                s.PlayerPage = 0;
                s.LoadoutPage = 0;
                s.SelectedLoadoutId = data != null ? data.DefaultLoadoutId : "default";
                uiState[adminId] = s;
            }

            if (string.IsNullOrEmpty(s.SelectedLoadoutId))
                s.SelectedLoadoutId = data != null ? data.DefaultLoadoutId : "default";

            return s;
        }

        #endregion

        #region Loadout Logic

        private Loadout GetPlayerLoadout(ulong userId)
        {
            string id;
            if (data.PlayerLoadout.TryGetValue(userId, out id))
            {
                id = NormalizeId(id);
                Loadout lo;
                if (data.Loadouts.TryGetValue(id, out lo)) return lo;
            }

            if (!string.IsNullOrEmpty(data.DefaultLoadoutId))
            {
                Loadout def;
                if (data.Loadouts.TryGetValue(data.DefaultLoadoutId, out def)) return def;
            }

            return null;
        }

        private void ApplyLoadout(BasePlayer player, Loadout loadout)
        {
            if (player == null || loadout == null) return;

            if (loadout.ClearInventoryBeforeGive)
                player.inventory.Strip();

            GiveItems(player, loadout.Items);

            player.inventory.ServerUpdate(0f);
            player.SendNetworkUpdateImmediate();
        }

        private void GiveItems(BasePlayer player, List<LoadoutItem> items)
        {
            if (items == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                LoadoutItem entry = items[i];
                if (entry == null) continue;
                if (string.IsNullOrEmpty(entry.Shortname)) continue;

                ItemDefinition def = ItemManager.FindItemDefinition(entry.Shortname.Trim());
                if (def == null)
                {
                    PrintWarning("Unknown item shortname: '" + entry.Shortname + "'");
                    continue;
                }

                ItemContainer container = GetContainer(player, entry.Container);
                if (container == null) continue;

                int total = entry.Amount < 1 ? 1 : entry.Amount;
                int stackSize = Math.Max(1, def.stackable);
                bool triedSlot = false;

                while (total > 0)
                {
                    int give = Math.Min(total, stackSize);
                    total -= give;

                    Item item = ItemManager.Create(def, give, entry.SkinId);
                    if (item == null) break;

                    if (entry.Condition > 0f)
                        item.condition = Mathf.Clamp(entry.Condition, 0f, item.maxCondition);

                    bool moved = false;
                    if (entry.Slot >= 0 && !triedSlot)
                    {
                        triedSlot = true;
                        moved = item.MoveToContainer(container, entry.Slot, true);
                    }

                    if (!moved) moved = item.MoveToContainer(container, -1, true);

                    if (!moved)
                        item.Drop(player.transform.position + new Vector3(0f, 1f, 0f), player.GetDropVelocity());
                }
            }
        }

        private ItemContainer GetContainer(BasePlayer player, string containerName)
        {
            string c = (containerName ?? "main").Trim().ToLowerInvariant();
            if (c == "wear" || c == "clothes" || c == "clothing") return player.inventory.containerWear;
            if (c == "belt" || c == "hotbar") return player.inventory.containerBelt;
            return player.inventory.containerMain;
        }

        private Loadout CaptureFromPlayer(BasePlayer admin)
        {
            List<LoadoutItem> items = new List<LoadoutItem>();

            CaptureContainer(admin.inventory.containerWear, "wear", items);
            CaptureContainer(admin.inventory.containerBelt, "belt", items);
            CaptureContainer(admin.inventory.containerMain, "main", items);

            Loadout l = new Loadout();
            l.ClearInventoryBeforeGive = cfg.DefaultClearInventoryBeforeGive;
            l.Items = items;
            return l;
        }

        private void CaptureContainer(ItemContainer container, string name, List<LoadoutItem> outItems)
        {
            if (container == null || container.itemList == null) return;

            for (int i = 0; i < container.itemList.Count; i++)
            {
                Item item = container.itemList[i];
                if (item == null || item.info == null) continue;

                LoadoutItem li = new LoadoutItem();
                li.Shortname = item.info.shortname;
                li.Amount = item.amount;
                li.SkinId = item.skin;
                li.Container = name;
                li.Slot = item.position;
                li.Condition = item.condition;

                outItems.Add(li);
            }
        }

        private void EnsureDefaultLoadoutExists()
        {
            if (string.IsNullOrEmpty(data.DefaultLoadoutId))
                data.DefaultLoadoutId = "default";

            if (!data.Loadouts.ContainsKey(data.DefaultLoadoutId))
            {
                Loadout l = new Loadout();
                l.ClearInventoryBeforeGive = true;
                l.Items = new List<LoadoutItem>
                {
                    new LoadoutItem { Shortname = "rock", Amount = 1, Container = "belt", Slot = 0 },
                    new LoadoutItem { Shortname = "torch", Amount = 1, Container = "belt", Slot = 1 }
                };

                data.Loadouts[data.DefaultLoadoutId] = l;
                SaveData();
            }
        }

        #endregion

        #region Data/Config

        private bool IsAdminAllowed(BasePlayer player)
        {
            if (player == null) return false;

            if (!cfg.RequirePermissionForUI)
                return player.IsAdmin;

            return permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("LoadoutController_Data");
            }
            catch
            {
                data = null;
            }

            if (data == null) data = new StoredData();
            if (data.Loadouts == null) data.Loadouts = new Dictionary<string, Loadout>(StringComparer.OrdinalIgnoreCase);
            if (data.PlayerLoadout == null) data.PlayerLoadout = new Dictionary<ulong, string>();

            if (string.IsNullOrEmpty(data.DefaultLoadoutId))
                data.DefaultLoadoutId = cfg.DefaultLoadoutId;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("LoadoutController_Data", data, true);
        }

        private void LoadConfigValues()
        {
            try
            {
                cfg = Config.ReadObject<ConfigData>();
            }
            catch
            {
                cfg = null;
            }

            if (cfg == null) cfg = new ConfigData();
            if (string.IsNullOrEmpty(cfg.DefaultLoadoutId)) cfg.DefaultLoadoutId = "default";

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            cfg = new ConfigData();
            cfg.RequirePermissionForUI = true;
            cfg.GiveDelaySeconds = 1.0f;
            cfg.UiRows = 12;
            cfg.AutoAssignDefaultOnFirstJoin = true;
            cfg.DefaultLoadoutId = "default";
            cfg.DefaultClearInventoryBeforeGive = true;
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(cfg, true);
        }

        private class ConfigData
        {
            [JsonProperty("Require loadoutcontroller.admin permission for UI (else IsAdmin)")]
            public bool RequirePermissionForUI;

            [JsonProperty("Give delay seconds after respawn")]
            public float GiveDelaySeconds;

            [JsonProperty("UI rows per list page")]
            public int UiRows;

            [JsonProperty("Auto-assign default loadout on first join")]
            public bool AutoAssignDefaultOnFirstJoin;

            [JsonProperty("Default loadout id")]
            public string DefaultLoadoutId;

            [JsonProperty("When saving from admin inventory: clear inventory before give")]
            public bool DefaultClearInventoryBeforeGive;
        }

        private class StoredData
        {
            [JsonProperty("DefaultLoadoutId")]
            public string DefaultLoadoutId = "default";

            [JsonProperty("Loadouts")]
            public Dictionary<string, Loadout> Loadouts = new Dictionary<string, Loadout>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("PlayerLoadout")]
            public Dictionary<ulong, string> PlayerLoadout = new Dictionary<ulong, string>();
        }

        private class Loadout
        {
            [JsonProperty("Clear inventory before give")]
            public bool ClearInventoryBeforeGive = true;

            [JsonProperty("Items")]
            public List<LoadoutItem> Items = new List<LoadoutItem>();
        }

        private class LoadoutItem
        {
            [JsonProperty("Shortname")]
            public string Shortname;

            [JsonProperty("Amount")]
            public int Amount = 1;

            [JsonProperty("SkinId")]
            public ulong SkinId = 0;

            [JsonProperty("Container (wear|belt|main)")]
            public string Container = "main";

            [JsonProperty("Slot (-1 = any)")]
            public int Slot = -1;

            [JsonProperty("Condition (0 = default)")]
            public float Condition = 0f;
        }

        private class UiState
        {
            public ulong SelectedPlayer;
            public string SelectedLoadoutId;
            public int PlayerPage;
            public int LoadoutPage;
        }

        private string NormalizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            raw = raw.Trim();

            char[] arr = raw.ToCharArray();
            for (int i = 0; i < arr.Length; i++)
            {
                char ch = arr[i];
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                    continue;
                arr[i] = '_';
            }
            return new string(arr);
        }

        #endregion
    }
}