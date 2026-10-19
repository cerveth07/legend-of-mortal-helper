using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Front-end parity with the previous panel: same title, five navigation pages,
    // the same two sub-tab groups, and the same wording for controls that exist.
    // Sections the rewrite has not reached yet stay visible but disabled and are
    // labelled as unported so the layout does not lie about what is wired up.
    internal sealed class MainPanel
    {
        private static readonly string[] Pages = { "剧情辅助", "战斗辅助", "养成辅助", "数值编辑", "存档设置" };
        private static readonly string[] StoryViews = { "剧情辅助", "单挑回想" };
        private static readonly string[] GrowthViews = { "养成辅助", "人际关系" };
        private static readonly string[] Lovers = { "自己", "小师妹", "瑞杏", "叶云裳", "虞小梅", "上官萤", "夏侯兰", "郁竹", "魏菊", "龙湘" };
        private static readonly HashSet<string> Editable = new HashSet<string>(new[] {
            "銀兩","個人貢獻度","門派資產","門派名聲","門派人數","向心力","心理衛生","武學點數","鍛造","毒藥",
            "體力","內力","武功拳掌","武功暗器","武功刀劍","輕功","學問","嘴力","抗毒","抗麻",
            "性情","處世","道德","修養","命運","陰陽內功" });
        private static readonly Dictionary<string, string> StatNames = new Dictionary<string, string> {
            { "銀兩", "金钱" }, { "個人貢獻度", "个人贡献" }, { "門派資產", "门派资产" }, { "門派名聲", "门派名声" },
            { "門派人數", "门派人数" }, { "向心力", "向心力" }, { "心理衛生", "心情" }, { "武學點數", "武学点数" },
            { "鍛造", "锻造点数" }, { "毒藥", "炼丹点数" }, { "體力", "体力" }, { "內力", "内力" },
            { "武功拳掌", "拳掌" }, { "武功暗器", "暗器" }, { "武功刀劍", "刀剑" }, { "輕功", "轻功" },
            { "學問", "学问" }, { "嘴力", "嘴力" }, { "抗毒", "抗毒" }, { "抗麻", "抗麻" },
            { "性情", "性情" }, { "處世", "处世" }, { "道德", "道德" }, { "修養", "修养" },
            { "命運", "命运" }, { "陰陽內功", "阴阳内功" } };

        private readonly Plugin plugin;
        private Rect bounds;
        private bool visible;
        private int page, storyView, growthView, loverIndex;
        private Vector2 scroll;
        private GameStat selected;
        private int expectedValue;
        private GameItemType itemType = GameItemType.雜物;
        private ItemData selectedItem;
        private int expectedItemCount;
        private string itemValue = "", itemSearch = "", value = "", search = "";
        private bool showAllItems;
        private RelationshipStat relationship;
        private string relationshipValue = "";
        private int expectedRelationship;
        private GameObject blocker;
        private GUISkin skin;
        private bool resizing, loggedHidden;
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
                // The panel draws its own backdrop so the opacity setting can apply.
                skin.window.normal.background = null;
                skin.window.hover.background = null;
                skin.window.active.background = null;
            }
            GUI.skin = skin;
            try { DrawWindow(); } finally { GUI.skin = previous; }
        }

        private void DrawWindow()
        {
            if (!Visible)
            {
                if (!loggedHidden) { loggedHidden = true; plugin.Log("Collapsed badge: " + Describe()); }
                Rect badge = new Rect(plugin.Options.BadgeX.Value, plugin.Options.BadgeY.Value, 34, 104);
                Color previousColor = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, plugin.Options.Opacity.Value);
                GUI.DrawTexture(badge, Texture2D.whiteTexture);
                GUI.color = previousColor;
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 20, richText = false };
                style.normal.textColor = Color.white;
                if (GUI.Button(badge, "萬\n象\n錄", new GUIStyle(style) { normal = { background = null }, hover = { background = null }, active = { background = null } }))
                    Visible = true;
                return;
            }
            EnsureBlocker();
            if (lastScreenWidth != Screen.width || lastScreenHeight != Screen.height)
            {
                lastScreenWidth = Screen.width;
                lastScreenHeight = Screen.height;
                bounds.width = Mathf.Min(bounds.width, Mathf.Max(320f, Screen.width * 0.96f));
                bounds.height = Mathf.Min(bounds.height, Mathf.Max(240f, Screen.height * 0.96f));
            }
            bounds.width = Mathf.Clamp(bounds.width, Mathf.Min(320f, Screen.width), Mathf.Max(320f, Screen.width * 0.96f));
            bounds.height = Mathf.Clamp(bounds.height, Mathf.Min(240f, Screen.height), Mathf.Max(240f, Screen.height * 0.96f));
            bounds.x = Mathf.Clamp(bounds.x, 0, Mathf.Max(0, Screen.width - bounds.width));
            bounds.y = Mathf.Clamp(bounds.y, 0, Mathf.Max(0, Screen.height - bounds.height));
            Color previous = GUI.color;
            GUI.color = new Color(0.07f, 0.07f, 0.07f, Mathf.Clamp(plugin.Options.Opacity.Value, 0.65f, 1f));
            GUI.DrawTexture(bounds, Texture2D.whiteTexture);
            GUI.color = previous;
            bounds = GUILayout.Window(0x434D48, bounds, Contents, "活侠·万象录");
            if (Event.current.type == EventType.MouseUp) SaveBounds();
        }

        private void Contents(int id)
        {
            GUILayout.BeginHorizontal();
            plugin.Options.Enabled.Value = GUILayout.Toggle(plugin.Options.Enabled.Value, "万象录功能总开关");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("收起", GUILayout.Width(70))) Visible = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("上次操作：" + plugin.Status, Subtle());
            page = GUILayout.Toolbar(page, Pages);
            scroll = GUILayout.BeginScrollView(scroll);
            bool previousEnabled = GUI.enabled;
            // The old panel kept its save settings usable while the main features
            // were paused; keep that exception even though the page is a placeholder.
            if (page != 4) GUI.enabled = plugin.Options.Enabled.Value && plugin.Gate.CanInteract;
            switch (page)
            {
                case 0: StoryPage(); break;
                case 1: BattlePage(); break;
                case 2: GrowthPage(); break;
                case 3: NumericPage(); break;
                default: SavePage(); break;
            }
            GUI.enabled = previousEnabled;
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            GUILayout.Label(plugin.Options.Toggle.Value + " 展开/收起 · 拖动标题移动 · 右下角缩放", Subtle());
            Rect handle = new Rect(bounds.width - 24, bounds.height - 24, 24, 24);
            GUI.Label(handle, "◢");
            Event e = Event.current;
            if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition))
            { resizing = true; resizeStart = GUIUtility.GUIToScreenPoint(e.mousePosition); sizeStart = bounds.size; e.Use(); }
            if (resizing && e.type == EventType.MouseDrag)
            { bounds.size = sizeStart + GUIUtility.GUIToScreenPoint(e.mousePosition) - resizeStart; e.Use(); }
            if (resizing && e.type == EventType.MouseUp) { resizing = false; SaveBounds(); e.Use(); }
            GUI.DragWindow(new Rect(0, 0, bounds.width - 60, 22));
        }

        private void StoryPage()
        {
            storyView = GUILayout.Toolbar(storyView, StoryViews);
            if (storyView == 1) { Recall(); return; }
            Section("天命控制");
            plugin.Options.Dice.Value = GUILayout.Toggle(plugin.Options.Dice.Value, "显示点数输入并允许点击结果选项");
            GUILayout.BeginHorizontal();
            GUILayout.Label("基础点数", GUILayout.Width(80));
            plugin.Options.Point.Value = GUILayout.TextField(plugin.Options.Point.Value, 8);
            if (GUILayout.Button("清空（自然掷骰）", GUILayout.Width(150))) plugin.Options.Point.Value = "";
            GUILayout.EndHorizontal();
            Note("清空点数恢复随机。指定值按每次判定范围校验，重掷仍使用原生命运点。");
            Section("选项提示");
            plugin.Options.Hints.Value = GUILayout.Toggle(plugin.Options.Hints.Value, "选项提示（含剧透）");
            plugin.Options.ShowEffects.Value = GUILayout.Toggle(plugin.Options.ShowEffects.Value, "显示效果");
            plugin.Options.ShowConditions.Value = GUILayout.Toggle(plugin.Options.ShowConditions.Value, "显示条件");
            Note("首次安装默认关闭；关闭总开关后保留两个子项的选择。收起面板后悬停选项生效。");
            GUILayout.Label("当前可对应的分支：" + HintAdapter.Count);
            Section("分支记录");
            Unported("显示已走分支标记", "记录已确认分支需要独立的周目记录组件，本候选尚未接入。");
            Section("高级工具（诊断与强制事件）");
            Unported("检查点更新提醒（剧情标记，不是存档点）", "尚需剧情诊断与强制事件接口。");
            Unported("强制事件 / 取消待执行事件与任务", "跳过前置条件会直接影响剧情走向，需实机验证后再开放。");
            Note("开发状态：主面板、骰子控制、受限提示、属性/好感/物品/速度、单挑与群战辅助已接入；恢复适配未完成。");
        }

        private void Recall()
        {
            Section("单挑回想");
            Note("记录当前或最近一场单挑的对白，最多 500 条；回想不暂停战斗。");
            if (DuelTools.Dialogue.Count == 0) { GUILayout.Label("本场还没有记录。"); return; }
            foreach (string line in DuelTools.Dialogue) GUILayout.Label(line, Wrap());
        }

        private void BattlePage()
        {
            Section("单挑辅助");
            Switch(plugin.Options.DuelHealth, "主角生命不减少");
            Note("阻止战斗扣血和持续伤害；保留剧情直接改值及强制胜负。");
            Switch(plugin.Options.DuelStamina, "单挑无限气力");
            Note("保持单挑行动所需气力，不修改周目内力属性。");
            Switch(plugin.Options.DuelDodge, "主角最高闪避率");
            Note("提高原生闪避率，不能避开必中特技或剧情结果。");
            Switch(plugin.Options.DuelDamage, "敌方受到有效伤害时一击必杀");
            Note("保留剧情最低生命保护与强制胜负。仅单挑生效；启用或战中切换后本场不能再保存单挑回合。");
            Section("群战辅助");
            Switch(plugin.Options.GroupHealth, "主角生命不减少");
            Note("仍可能硬直；友军生命和剧情强制退场沿用原生规则。");
            Switch(plugin.Options.GroupMana, "主角气力保持充足");
            Note("作用于群战气力条。");
            Switch(plugin.Options.GroupCooldown, "群战技能栏立即冷却");
            Note("保留原生施放限制与演出。");
            Switch(plugin.Options.GroupFreeze, "冻结群战剩余时间");
            Note("原生倒计时延长，增援与事件照常推进；按计时归零获胜的关卡请关闭。");
            Switch(plugin.Options.GroupDown, "敌方受伤即倒地");
            Note("含友军攻击和持续伤害；保留敌方闪避与剧情无敌，不影响中立及友方。");
        }

        private void GrowthPage()
        {
            growthView = GUILayout.Toolbar(growthView, GrowthViews);
            if (growthView == 1) { Relationships(); return; }
            Note("编辑已登场角色的好感度与心上人，正常存档后保留。");
            Section("练功");
            Unported("练功消耗不减", "保留练功点数、本次次数和负向升级消耗；武功修炼条件仍须满足。");
            Unported("快速练功", "每次正常练功完成当前一级进度，仍执行原生升级奖励与条件。");
            Section("锻造与炼丹");
            Unported("锻造炼丹消耗不减", "锻造炼丹不扣点数、金钱与本次次数，资源为零也可操作；不解锁研发条件。");
            Unported("快速锻造炼丹", "每次操作完成当前一级进度；保留原生等级上限与奖励。");
            Section("日常行动");
            Unported("无限日常行动点", "行动点至少为 1 且不减，日期不会因行动用完自动前进。");
            Section("最高好感");
            Unported("已登场角色保持各自好感上限", "不解锁未登场角色；正常存档后保留，关闭不会还原。");
            Note("以上四组属于养成辅助，旧版已实现；本候选尚未接入，界面保留原位置。");
        }

        private void Relationships()
        {
            Section("好感编辑");
            if (!GameAccess.CanEdit)
            {
                GUILayout.Label("请在周目剧情或自由行动中编辑；先关闭商店、升级等面板，并等待加载完成。", Wrap());
                relationship = null;
            }
            else
            {
                var list = PlayerStatManagerData.Instance.Relationships.List;
                foreach (var item in list)
                {
                    if (item == null || !item.Active) continue;
                    if (GUILayout.Button((relationship == item ? "● " : "") + item.Type + "   " + item.Value))
                    { relationship = item; expectedRelationship = item.Value; relationshipValue = item.Value.ToString(); }
                }
                if (relationship != null)
                {
                    int maximum = GameAccess.Field<int>(relationship, "_max");
                    GUILayout.Label("修改 " + relationship.Type + " 好感：0～" + maximum);
                    relationshipValue = GUILayout.TextField(relationshipValue, 12);
                    if (GUILayout.Button("应用好感度"))
                    {
                        int parsed;
                        if (!relationship.Active || !list.Contains(relationship) || relationship.Value != expectedRelationship)
                        { plugin.Status = "人物数据已变化，请重新选择角色。"; relationship = null; }
                        else if (!int.TryParse(relationshipValue, out parsed) || parsed < 0 || parsed > maximum)
                            plugin.Status = "输入不在允许范围内。";
                        else
                        {
                            plugin.Gate.NotifyMutation("修改角色好感");
                            relationship.SetValue(parsed);
                            expectedRelationship = relationship.Value;
                            plugin.Status = "好感已修改；正常存档后保留。";
                        }
                    }
                }
            }
            Note("修改当前周目，正常存档后保留。不会补齐恋爱事件、结缘条件或解锁未登场角色。");
            Section("心上人");
            loverIndex = GUILayout.SelectionGrid(loverIndex, Lovers, 5);
            GUI.enabled = false;
            GUILayout.Button("应用心上人");
            GUI.enabled = true;
            Note("旧版可选择九位候选之一或「自己」；本候选尚未接入，仅保留界面位置。");
        }

        private void NumericPage()
        {
            Section("属性");
            Note("修改会影响进度；请使用备份周目。当前仅在自由活动界面空闲时开放。");
            StatsBody();
            Section("物品数量");
            ItemsBody();
            Section("游戏速度");
            GUILayout.BeginHorizontal();
            float speed = GUILayout.HorizontalSlider(plugin.Options.Speed.Value, 0.25f, 5f);
            GUILayout.Label(speed.ToString("0.00") + " 倍", GUILayout.Width(70));
            if (GUILayout.Button("恢复 1 倍", GUILayout.Width(90))) plugin.ApplySpeed(1f);
            else if (Mathf.Abs(speed - plugin.Options.Speed.Value) > 0.02f) plugin.ApplySpeed(speed);
            GUILayout.EndHorizontal();
            Note("只改引擎时间倍率；原生暂停、慢动作与快进各有自己的状态。");
            Section("其他");
            Unported("解除修炼点数消耗上限", "旧版数值编辑项，尚未接入。");
        }

        private void StatsBody()
        {
            bool safe = GameAccess.CanEdit && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Free";
            if (!safe) { GUILayout.Label("请先进入自由活动场景，关闭游戏内交互面板。", Wrap()); selected = null; return; }
            var player = PlayerStatManagerData.Instance;
            GUILayout.BeginHorizontal(); GUILayout.Label("筛选", GUILayout.Width(45)); search = GUILayout.TextField(search, 80); GUILayout.EndHorizontal();
            foreach (var stat in player.Stats.List)
            {
                if (stat == null || !Editable.Contains(stat.StatType.ToString())) continue;
                string name = DisplayName(stat);
                if (!string.IsNullOrEmpty(search) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button((selected == stat ? "● " : "") + name + "   " + stat.Value + " / 最终 " + stat.FinalValue))
                { selected = stat; expectedValue = stat.Value; value = expectedValue.ToString(); }
            }
            if (selected == null) return;
            GUILayout.Space(12);
            GUILayout.Label(DisplayName(selected) + "：允许 " + selected.Min + "～" + selected.Max);
            value = GUILayout.TextField(value, 12);
            if (GUILayout.Button("应用到所选属性"))
            {
                int number;
                if (!GameAccess.CanEdit || selected.Value != expectedValue || !player.Stats.List.Contains(selected))
                { plugin.Status = "状态已改变，请重新选择属性。"; selected = null; return; }
                if (!int.TryParse(value, out number) || number < selected.Min || number > selected.Max)
                { plugin.Status = "输入值不在允许范围内。"; return; }
                plugin.Gate.NotifyMutation("修改属性 " + selected.StatType);
                selected.SetValue(number); expectedValue = selected.Value;
                plugin.Status = "已修改 " + DisplayName(selected) + "；请通过原生存档保留。";
            }
        }

        private void ItemsBody()
        {
            if (!GameAccess.CanEdit) { GUILayout.Label("请先进入自由活动场景并关闭游戏内交互面板。", Wrap()); selectedItem = null; return; }
            var database = ItemDatabase.Instance;
            if (database == null) { GUILayout.Label("物品数据库尚未就绪。", Wrap()); return; }
            int tab = GUILayout.Toolbar((int)itemType - 1, new[] { "書籍", "雜物", "貴重品", "消耗品" });
            itemType = (GameItemType)(tab + 1);
            showAllItems = GUILayout.Toggle(showAllItems, "也列出数量为 0 的物品（等于直接生成）");
            GUILayout.BeginHorizontal(); GUILayout.Label("筛选", GUILayout.Width(45)); itemSearch = GUILayout.TextField(itemSearch, 80); GUILayout.EndHorizontal();
            ItemDataCollection collection = tab == 0 ? database.Books : tab == 1 ? database.Miscs
                : tab == 2 ? database.Special : database.Consumables;
            if (collection == null || collection.List == null) { GUILayout.Label("该分类物品列表尚未就绪。", Wrap()); return; }
            foreach (var item in collection.List)
            {
                if (item == null || (!showAllItems && item.CurrentCount <= 0)) continue;
                string label = ItemName(item) + "   " + item.CurrentCount;
                if (!string.IsNullOrEmpty(itemSearch) && label.IndexOf(itemSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button((selectedItem == item ? "● " : "") + label))
                { selectedItem = item; expectedItemCount = item.CurrentCount; itemValue = expectedItemCount.ToString(); }
            }
            if (selectedItem == null) return;
            GUILayout.Space(12);
            GUILayout.Label(ItemName(selectedItem) + "：当前 " + selectedItem.CurrentCount + "，可设为 0～999999");
            itemValue = GUILayout.TextField(itemValue, 12);
            if (GUILayout.Button("应用到所选物品"))
            {
                int number;
                if (!GameAccess.CanEdit || selectedItem.CurrentCount != expectedItemCount)
                { plugin.Status = "物品状态已改变，请重新选择。"; selectedItem = null; return; }
                if (!int.TryParse(itemValue, out number) || number < 0 || number > 999999)
                { plugin.Status = "输入不在允许范围内。"; return; }
                plugin.Gate.NotifyMutation("修改物品 " + selectedItem.Id);
                selectedItem.SetCount(number); expectedItemCount = selectedItem.CurrentCount;
                plugin.Status = "已修改物品数量；请通过原生存档保留。";
            }
        }

        private void SavePage()
        {
            Section("存档格数");
            GUILayout.BeginHorizontal();
            GUILayout.Label("允许范围 2～999", GUILayout.Width(140));
            GUI.enabled = false;
            GUILayout.TextField("", 4, GUILayout.Width(60));
            GUILayout.Button("保存格数", GUILayout.Width(90));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            Note("旧版可改存档格数并提示「格数已保存，重启游戏后生效」；本候选尚未接入。");
            Section("存档备注与排序");
            Unported("存档备注（选中存档后 F2 编辑）", "备注保存在实际存档目录的 SaveSlotComments.ini。");
            Unported("按最近存档排序（F3 切换）", "排序状态由独立存档组件维护。");
            Note("存读仍使用游戏原生系统菜单与编号槽。本页为占位，旧版对应功能尚未重新实现。");
        }

        private void Section(string title) { GUILayout.Space(10); GUILayout.Label(title, Header()); }

        private void Switch(ConfigEntry<bool> setting, string label)
        {
            bool next = GUILayout.Toggle(setting.Value, label);
            if (next != setting.Value) { plugin.Gate.NotifyMutation("切换 " + label); setting.Value = next; }
        }

        // A control the rewrite has not reached yet: shown disabled so the layout
        // matches the old panel without pretending the behaviour is wired up.
        private void Unported(string label, string description)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = false;
            GUILayout.Toggle(false, label);
            GUI.enabled = previousEnabled;
            Note(description);
        }

        private void Note(string text) { GUILayout.Label(text, Wrap()); }

        private static GUIStyle Wrap() { return new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false }; }
        private static GUIStyle Header() { return new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold }; }
        private static GUIStyle Subtle() { return new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true }; }

        private static string DisplayName(GameStat stat)
        {
            string name;
            return stat != null && StatNames.TryGetValue(stat.StatType.ToString(), out name) ? name : (stat == null ? "" : stat.Name);
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
            blocker = new GameObject("MortalHelper.PointerShield", typeof(Canvas), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(blocker);
            var canvas = blocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
            var child = new GameObject("Block", typeof(RectTransform), typeof(Image)); child.transform.SetParent(blocker.transform, false);
            var rect = (RectTransform)child.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; child.GetComponent<Image>().color = Color.clear;
        }

        internal void ClearSelection() { selected = null; relationship = null; selectedItem = null; }
        internal void Dispose()
        {
            if (blocker != null) UnityEngine.Object.Destroy(blocker);
            if (skin != null) UnityEngine.Object.Destroy(skin);
        }
    }
}
