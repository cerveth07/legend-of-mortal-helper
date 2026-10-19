using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    internal sealed class MainPanel
    {
        private readonly Plugin plugin;
        private Rect bounds;
        private bool visible;
        private int page;
        private Vector2 scroll;
        private GameStat selected;
        private int expectedValue;
        private GameItemType itemType = GameItemType.雜物;
        private ItemData selectedItem;
        private int expectedItemCount;
        private string itemValue = "", itemSearch = "";
        private string value = "", search = "";
        private RelationshipStat relationship;
        private string relationshipValue = "";
        private int expectedRelationship;
        private static readonly HashSet<string> Editable = new HashSet<string>(new[] {
            "銀兩","個人貢獻度","門派資產","門派名聲","門派人數","向心力","心理衛生","武學點數","鍛造","毒藥",
            "體力","內力","武功拳掌","武功暗器","武功刀劍","輕功","學問","嘴力","抗毒","抗麻",
            "性情","處世","道德","修養","命運","陰陽內功" });
        private GameObject blocker;
        private GUISkin skin;
        private bool resizing;
        private bool loggedHidden;
        private int lastScreenWidth, lastScreenHeight;
        private Vector2 resizeStart, sizeStart;
        internal bool Visible
        {
            get => visible;
            set { visible = value; if (blocker != null) blocker.SetActive(value); }
        }

        internal MainPanel(Plugin plugin)
        {
            this.plugin = plugin;
            bounds = new Rect(plugin.Options.X.Value, plugin.Options.Y.Value, plugin.Options.Width.Value, plugin.Options.Height.Value);
        }

        internal void Draw()
        {
            GUISkin previous = GUI.skin;
            if (skin == null)
            {
                skin = UnityEngine.Object.Instantiate(previous);
                foreach (var source in UnityEngine.Object.FindObjectsOfType<Text>())
                    if (source.font != null && source.font.HasCharacter('活')) { skin.font = source.font; break; }
                skin.button.fontSize = skin.label.fontSize = skin.toggle.fontSize = skin.textField.fontSize = 18;
                skin.window.fontSize = 20;
            }
            GUI.skin = skin;
            try { DrawWindow(); } finally { GUI.skin = previous; }
        }

        private void DrawWindow()
        {
            if (!Visible)
            {
                if (!loggedHidden)
                {
                    loggedHidden = true;
                    plugin.Log("Hidden affordance: " + Describe());
                }
                // Do not depend on the game skin or a CJK font here: before the
                // first text object exists the label would render as nothing.
                Rect entry = new Rect(12, Screen.height / 2f - 22, 94, 44);
                Color previous = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(entry, Texture2D.whiteTexture);
                GUI.color = previous;
                if (GUI.Button(entry, "Tab")) Visible = true;
                return;
            }
            EnsureBlocker();
            // Screen starts as a placeholder size and changes once the window is
            // real, so refit whenever it changes instead of trusting the awake value.
            if (lastScreenWidth != Screen.width || lastScreenHeight != Screen.height)
            {
                lastScreenWidth = Screen.width;
                lastScreenHeight = Screen.height;
                bounds.width = Mathf.Min(bounds.width, Mathf.Max(320f, Screen.width * 0.96f));
                bounds.height = Mathf.Min(bounds.height, Mathf.Max(240f, Screen.height * 0.96f));
            }
            bounds.width = Mathf.Clamp(bounds.width, Mathf.Min(320f, Screen.width), Mathf.Max(320f, Screen.width * 0.96f));
            bounds.height = Mathf.Clamp(bounds.height, Mathf.Min(240f, Screen.height), Mathf.Max(240f, Screen.height * 0.96f));
            bounds.x = Mathf.Clamp(bounds.x, 0, Mathf.Max(0,Screen.width - bounds.width));
            bounds.y = Mathf.Clamp(bounds.y, 0, Mathf.Max(0,Screen.height - bounds.height));
            bounds = GUILayout.Window(0x434D48, bounds, Contents, "活侠助手 · 独立重建 0.1.0");
            if (Event.current.type == EventType.MouseUp) SaveBounds();
        }

        private void Contents(int id)
        {
            GUILayout.BeginHorizontal();
            plugin.Options.Enabled.Value = GUILayout.Toggle(plugin.Options.Enabled.Value, "启用辅助");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("收起",GUILayout.Width(70))) Visible = false;
            GUILayout.EndHorizontal();
            page = GUILayout.Toolbar(page, new[] { "剧情", "单挑", "属性", "关系", "物品", "开发状态" });
            scroll = GUILayout.BeginScrollView(scroll);
            bool previousEnabled = GUI.enabled;
            if (page != 5) GUI.enabled = plugin.Options.Enabled.Value && plugin.Gate.CanInteract;
            if (page == 0) Story();
            else if (page == 1) Duel();
            else if (page == 2) Stats();
            else if (page == 3) Relationships();
            else if (page == 4) Items();
            else Diagnostics();
            GUI.enabled = previousEnabled;
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            GUILayout.Label(plugin.Status, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.Label(plugin.Options.Toggle.Value + " 展开/收起 · 拖动标题移动 · 右下角缩放");
            Rect handle = new Rect(bounds.width-24,bounds.height-24,24,24);
            GUI.Label(handle,"◢");
            Event e = Event.current;
            if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition))
            { resizing = true; resizeStart = GUIUtility.GUIToScreenPoint(e.mousePosition); sizeStart = bounds.size; e.Use(); }
            if (resizing && e.type == EventType.MouseDrag)
            { bounds.size = sizeStart + GUIUtility.GUIToScreenPoint(e.mousePosition) - resizeStart; e.Use(); }
            if (resizing && e.type == EventType.MouseUp) { resizing = false; SaveBounds(); e.Use(); }
            GUI.DragWindow(new Rect(0,0,bounds.width-60,22));
        }

        private void Story()
        {
            GUILayout.Label("原生轮盘");
            plugin.Options.Dice.Value = GUILayout.Toggle(plugin.Options.Dice.Value, "显示点数输入并允许点击结果选项");
            GUILayout.Label("清空点数恢复随机。指定值按每次判定范围校验，重掷仍使用原生命运点。", Wrap());
            GUILayout.Space(16);
            plugin.Options.Hints.Value = GUILayout.Toggle(plugin.Options.Hints.Value, "选项效果提示（含剧透）");
            GUILayout.Label("收起面板后悬停选项；仅展示可确定的直接调用和原生轮盘条件，无法对应时省略。",Wrap());
            GUILayout.Label("当前可对应的分支：" + HintAdapter.Count);
            GUILayout.Space(12); GUILayout.Label("本场 / 最近一场单挑回想（最多 500 条）");
            foreach (string line in DuelTools.Dialogue) GUILayout.Label(line,Wrap());
        }

        private void Duel()
        {
            Switch(plugin.Options.DuelHealth,"主角生命不减少");
            Switch(plugin.Options.DuelStamina,"主角气力不减少");
            Switch(plugin.Options.DuelDodge,"最高闪避率");
            Switch(plugin.Options.DuelDamage,"敌方有效受伤时一击必杀");
            GUILayout.Label("保留游戏剧情强制结果与最低生命限制。恢复期间所有辅助暂停；开关变化会通知恢复系统重新建立有效边界。",Wrap());
        }

        private void Switch(ConfigEntry<bool> setting, string label)
        {
            bool next = GUILayout.Toggle(setting.Value,label);
            if (next != setting.Value) { plugin.Gate.NotifyMutation("切换 " + label); setting.Value = next; }
        }

        private void Stats()
        {
            GUILayout.Label("修改会影响进度；请使用备份周目。当前仅在自由活动界面空闲时开放。",Wrap());
            bool safe = GameAccess.CanEdit && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Free";
            if (!safe) { GUILayout.Label("请先进入自由活动场景，关闭游戏内交互面板。",Wrap()); selected = null; return; }
            var player = PlayerStatManagerData.Instance;
            GUILayout.BeginHorizontal(); GUILayout.Label("筛选",GUILayout.Width(45)); search = GUILayout.TextField(search,80); GUILayout.EndHorizontal();
            foreach (var stat in player.Stats.List)
            {
                if (stat == null || !Editable.Contains(stat.StatType.ToString()) || (!string.IsNullOrEmpty(search) && (stat.Name ?? "").IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)) continue;
                if (GUILayout.Button((selected == stat ? "● " : "") + stat.Name + "   " + stat.Value + " / 最终 " + stat.FinalValue))
                { selected = stat; expectedValue = stat.Value; value = expectedValue.ToString(); }
            }
            if (selected == null) return;
            GUILayout.Space(12);
            GUILayout.Label(selected.Name + "：允许 " + selected.Min + "～" + selected.Max);
            value = GUILayout.TextField(value,12);
            if (GUILayout.Button("应用到所选属性"))
            {
                int number;
                if (!GameAccess.CanEdit || selected.Value != expectedValue || !player.Stats.List.Contains(selected))
                { plugin.Status = "状态已改变，请重新选择属性。"; selected = null; return; }
                if (!int.TryParse(value,out number) || number<selected.Min || number>selected.Max)
                { plugin.Status = "输入值不在允许范围内。"; return; }
                plugin.Gate.NotifyMutation("修改属性 " + selected.StatType);
                selected.SetValue(number); expectedValue = selected.Value;
                plugin.Status = "已修改 " + selected.Name + "；请通过原生存档保留。";
            }
        }

        private void Items()
        {
            GUILayout.Label("物品数量会直接影响剧情、交易与战斗；请使用备份周目。当前仅在自由活动界面空闲时开放。", Wrap());
            if (!GameAccess.CanEdit) { GUILayout.Label("请先进入自由活动场景并关闭游戏内交互面板。",Wrap()); selectedItem = null; return; }
            var database = ItemDatabase.Instance;
            if (database == null) { GUILayout.Label("物品数据库尚未就绪。",Wrap()); return; }
            int tab = GUILayout.Toolbar((int)itemType - 1, new[] { "書籍", "雜物", "貴重品", "消耗品" });
            itemType = (GameItemType)(tab + 1);
            GUILayout.BeginHorizontal(); GUILayout.Label("筛选",GUILayout.Width(45)); itemSearch = GUILayout.TextField(itemSearch,80); GUILayout.EndHorizontal();
            ItemDataCollection collection = tab == 0 ? database.Books : tab == 1 ? database.Miscs
                : tab == 2 ? database.Special : database.Consumables;
            if (collection == null || collection.List == null) { GUILayout.Label("该分类物品列表尚未就绪。",Wrap()); return; }
            foreach (var item in collection.List)
            {
                if (item == null) continue;
                string label = ItemName(item) + "   " + item.CurrentCount;
                if (!string.IsNullOrEmpty(itemSearch) && label.IndexOf(itemSearch,StringComparison.OrdinalIgnoreCase)<0) continue;
                if (GUILayout.Button((selectedItem == item ? "● " : "") + label))
                { selectedItem = item; expectedItemCount = item.CurrentCount; itemValue = expectedItemCount.ToString(); }
            }
            if (selectedItem == null) return;
            GUILayout.Space(12);
            GUILayout.Label(ItemName(selectedItem) + "：当前 " + selectedItem.CurrentCount + "，可设为 0～999999");
            itemValue = GUILayout.TextField(itemValue,12);
            if (GUILayout.Button("应用到所选物品"))
            {
                int number;
                if (!GameAccess.CanEdit || selectedItem.CurrentCount != expectedItemCount)
                { plugin.Status = "物品状态已改变，请重新选择。"; selectedItem = null; return; }
                if (!int.TryParse(itemValue,out number) || number<0 || number>999999)
                { plugin.Status = "输入不在允许范围内。"; return; }
                plugin.Gate.NotifyMutation("修改物品 " + selectedItem.Id);
                selectedItem.SetCount(number); expectedItemCount = selectedItem.CurrentCount;
                plugin.Status = "已修改物品数量；请通过原生存档保留。";
            }
        }

        private static string ItemName(ItemData item)
        {
            try
            {
                var manager = LocalizationManager.Instance;
                if (manager != null && manager.LocaleResolver != null)
                {
                    string text = manager.LocaleResolver.GetString(item.GetNameKey());
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
            catch (Exception) { }
            return item.Id;
        }

        private void Relationships()
        {
            if (!GameAccess.CanEdit) { GUILayout.Label("请先进入自由活动场景并关闭游戏内交互面板。",Wrap()); relationship = null; return; }
            var list = PlayerStatManagerData.Instance.Relationships.List;
            foreach (var item in list)
            {
                if (item == null || !item.Active) continue;
                if (GUILayout.Button((relationship == item ? "● " : "") + item.Type + "   " + item.Value))
                { relationship = item; expectedRelationship = item.Value; relationshipValue = item.Value.ToString(); }
            }
            if (relationship == null) return;
            int maximum = GameAccess.Field<int>(relationship,"_max");
            GUILayout.Label("修改 " + relationship.Type + " 好感：0～" + maximum);
            relationshipValue = GUILayout.TextField(relationshipValue,12);
            if (GUILayout.Button("应用好感"))
            {
                int parsed;
                if (!relationship.Active || !list.Contains(relationship) || relationship.Value != expectedRelationship)
                { plugin.Status = "角色状态已改变，请重新选择。"; relationship = null; return; }
                if (!int.TryParse(relationshipValue,out parsed) || parsed<0 || parsed>maximum)
                { plugin.Status = "输入不在允许范围内。"; return; }
                plugin.Gate.NotifyMutation("修改角色好感"); relationship.SetValue(parsed);
                expectedRelationship = relationship.Value; plugin.Status = "好感已修改；关闭面板后请正常存档。";
            }
        }

        private void Diagnostics()
        {
            GUILayout.Label("该版本是重建中的开发候选，尚未完成 Unity 实机验收。", Wrap());
            GUILayout.Space(10);
            GUILayout.Label("已接入：独立配置、主面板、骰子控制、受限脚本提示、属性/好感/物品数量编辑、单挑辅助、单挑回想。",Wrap());
            GUILayout.Label("恢复底层：固定字段文件格式、完整性检查、大小限制、单文件备份和恢复状态机。",Wrap());
            GUILayout.Label("剧情与单挑恢复尚未接入游戏，当前不会读取旧 .loms / .duel，也不会改写原生存档。",Wrap());
            GUILayout.Space(10);
            GUILayout.Label("群战/养成辅助、分支记录、心上人编辑、槽位扩展正在逐项重新实现与审核；本候选不宣称功能齐全。",Wrap());
            GUILayout.Label("面板不暂停游戏；请在稳定场景操作。",Wrap());
        }

        private static GUIStyle Wrap() { return new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false }; }

        // Diagnostics for the window/panel mismatch: IMGUI works in game screen
        // coordinates, which do not always match the window pixel size.
        internal string Describe()
        {
            return "visible=" + visible + " bounds=" + bounds.x.ToString("0") + "," + bounds.y.ToString("0") +
                " " + bounds.width.ToString("0") + "x" + bounds.height.ToString("0") +
                " screen=" + Screen.width + "x" + Screen.height;
        }
        private void SaveBounds()
        {
            plugin.Options.X.Value = bounds.x; plugin.Options.Y.Value = bounds.y;
            plugin.Options.Width.Value = bounds.width; plugin.Options.Height.Value = bounds.height;
        }

        private void EnsureBlocker()
        {
            if (blocker != null) { blocker.SetActive(Visible); return; }
            blocker = new GameObject("MortalHelper.PointerShield",typeof(Canvas),typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(blocker);
            var canvas = blocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
            var child = new GameObject("Block",typeof(RectTransform),typeof(Image)); child.transform.SetParent(blocker.transform,false);
            var rect = (RectTransform)child.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; child.GetComponent<Image>().color = Color.clear;
        }

        internal void ClearSelection() { selected = null; relationship = null; }
        internal void Dispose()
        {
            if (blocker != null) UnityEngine.Object.Destroy(blocker);
            if (skin != null) UnityEngine.Object.Destroy(skin);
        }
    }
}
