using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Visual shell reproduced from the previous panel: a parchment sheet with
    // corner accents, ink wash, scroll rolls, the five-page tab row with a paper
    // highlight, switch rows, footer opacity slider, resize grip and the vertical
    // KaiTi badge. Feature bodies below are this project's own implementations.
    internal sealed class MainPanel
    {
        private const float ExpandDuration = .22f;
        private const float BadgeWidth = 52f;
        private const float BadgeHeight = 150f;
        private const float MinWidth = 420f;
        private const float MinHeight = 360f;

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
        private readonly PanelStyle style = new PanelStyle();
        private Rect windowRect;
        private Rect shownRect;
        private Vector2 badgePosition;
        private bool open, initialized;
        private float scale;
        private float opacity;
        private bool animating, closing;
        private float progress;
        private Rect animFrom, animTo;
        private float tabTransition = 1f;
        private int page, storyView, growthView, loverIndex;
        private readonly Vector2[] scroll = new Vector2[5];
        private GameStat selected;
        private int expectedValue;
        private GameItemType itemType = GameItemType.雜物;
        private ItemData selectedItem;
        private int expectedItemCount;
        private string itemValue = "", itemSearch = "", value = "", search = "";
        private bool showAllItems, loggedBadge;
        private string slotComment = "", commentSlot, slotCountText = "";
        private int recoverySlot = 1;
        private RelationshipStat relationship;
        private string relationshipValue = "";
        private int expectedRelationship;
        private GameObject blocker;
        private bool resizing, draggingBadge, badgeMoved, draggingWindow;
        private int resizeEdges, hoverEdges;
        private Vector2 dragStartMouse;
        private Rect dragStartRect;
        private Vector2 dragStart, sizeStart;
        private int lastWidth, lastHeight;

        internal bool Visible
        {
            get => open;
            set
            {
                if (value)
                {
                    if (open) return;
                    animFrom = BadgeRect();
                    animTo = ClampWindow();
                    open = true;
                    closing = false;
                }
                else
                {
                    if (!open || closing) return;
                    animFrom = shownRect.width > 1f ? shownRect : ClampWindow();
                    animTo = BadgeRect();
                    closing = true;
                }
                animating = true;
                progress = 0f;
            }
        }

        // Opened by F2 so the comment editor is one keystroke away from the save
        // list, matching the previous panel's shortcut.
        internal void OpenSaveSettings()
        {
            page = 4;
            commentSlot = null;
            Visible = true;
        }

        internal MainPanel(Plugin plugin)
        {
            this.plugin = plugin;
            windowRect = new Rect(plugin.Options.X.Value, plugin.Options.Y.Value,
                Mathf.Max(MinWidth, plugin.Options.Width.Value), Mathf.Max(MinHeight, plugin.Options.Height.Value));
            badgePosition = new Vector2(plugin.Options.BadgeX.Value, plugin.Options.BadgeY.Value);
            opacity = Mathf.Clamp(plugin.Options.Opacity.Value, .4f, 1f);
        }

        internal void Draw()
        {
            if (!initialized)
            {
                style.Build();
                style.SetOpacity(opacity);
                initialized = true;
            }
            EnsureScale();
            if (opacity != Mathf.Clamp(plugin.Options.Opacity.Value, .65f, 1f))
            {
                opacity = Mathf.Clamp(plugin.Options.Opacity.Value, .4f, 1f);
                style.SetOpacity(opacity);
            }
            GUISkin previous = GUI.skin;
            GUI.skin = style.Skin;
            try
            {
                if (open || closing) DrawPanel();
                if (!open) DrawBadge();
            }
            finally { GUI.skin = previous; }
        }

        private void EnsureScale()
        {
            float next = Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), .85f, 1.35f);
            if (style.UiFont == null)
            {
                style.UiFont = CreateBodyFont();
                plugin.Log("Body font: " + (style.UiFont == null ? "game default" : style.UiFont.name));
            }
            if (Mathf.Abs(next - scale) < .001f && lastWidth == Screen.width && lastHeight == Screen.height &&
                style.Skin != null) return;
            scale = next; lastWidth = Screen.width; lastHeight = Screen.height;
            style.Refresh(scale, style.UiFont);
            style.SetOpacity(opacity);
        }

        // The game ships only a calligraphic CJK font plus Arial, and Unity does not
        // fall back glyphs for dynamic fonts. Load a normal sans CJK face from the
        // OS for panel text; the Kai font stays on the vertical badge title.
        private static Font CreateBodyFont()
        {
            try
            {
                var font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei", "黑体",
                    "Noto Sans CJK SC", "Source Han Sans SC", "PingFang SC", "Meiryo", "Yu Gothic UI"
                }, 18);
                if (font != null) return font;
            }
            catch (Exception) { }
            return FindGameFont();
        }

        private static Font FindGameFont()
        {
            Font fallback = null;
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (font == null || !font.HasCharacter('天')) continue;
                if (IsCalligraphic(font.name)) { if (fallback == null) fallback = font; continue; }
                return font;
            }
            return fallback;
        }

        private static bool IsCalligraphic(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("kai") || lower.Contains("brush") || lower.Contains("callig") || name.Contains("楷") || name.Contains("行書") || name.Contains("行书");
        }

        private Rect BadgeRect()
        {
            return new Rect(badgePosition.x, badgePosition.y,
                Mathf.Min(BadgeWidth * scale, Mathf.Max(1f, Screen.width - 16f)),
                Mathf.Min(BadgeHeight * scale, Mathf.Max(1f, Screen.height - 16f)));
        }

        private Rect ClampWindow() { return Clamp(windowRect); }

        private Rect Clamp(Rect source)
        {
            float minWidth = Mathf.Min(MinWidth * scale, Mathf.Max(1f, Screen.width - 24f));
            float minHeight = Mathf.Min(MinHeight * scale, Mathf.Max(1f, Screen.height - 24f));
            Rect rect = source;
            rect.width = Mathf.Clamp(rect.width, minWidth, Mathf.Max(minWidth, Screen.width - 24f));
            rect.height = Mathf.Clamp(rect.height, minHeight, Mathf.Max(minHeight, Screen.height - 24f));
            rect.x = Mathf.Clamp(rect.x, 8f, Mathf.Max(8f, Screen.width - rect.width - 8f));
            rect.y = Mathf.Clamp(rect.y, 8f, Mathf.Max(8f, Screen.height - rect.height - 8f));
            return rect;
        }


        private static float EaseOutCubic(float t) { float p = 1f - t; return 1f - p * p * p; }

        // Four-edge and corner resizing plus header dragging, polled from real
        // mouse input. IMGUI cannot reliably own these gestures across the
        // Layout/Repaint passes, which is why the previous panel did the same.
        private void PollWindowPointer()
        {
            Vector3 raw = Input.mousePosition;
            Vector2 mouse = new Vector2(raw.x, Screen.height - raw.y);
            Rect rect = windowRect;
            float hit = 16f * scale;
            if (Input.GetMouseButtonDown(0))
            {
                int edges = NearEdges(rect, mouse, hit);
                if (edges != 0) { resizeEdges = edges; dragStartMouse = mouse; dragStartRect = rect; }
                else if (new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 44f * scale), 42f * scale).Contains(mouse))
                { draggingWindow = true; dragStartMouse = mouse; dragStartRect = rect; }
            }
            if (Input.GetMouseButton(0) && (resizeEdges != 0 || draggingWindow))
            {
                Vector2 delta = mouse - dragStartMouse;
                Rect next = dragStartRect;
                if (draggingWindow) { next.x += delta.x; next.y += delta.y; }
                else
                {
                    if ((resizeEdges & 1) != 0) { next.x = dragStartRect.x + delta.x; next.width = dragStartRect.width - delta.x; }
                    if ((resizeEdges & 2) != 0) next.width = dragStartRect.width + delta.x;
                    if ((resizeEdges & 4) != 0) { next.y = dragStartRect.y + delta.y; next.height = dragStartRect.height - delta.y; }
                    if ((resizeEdges & 8) != 0) next.height = dragStartRect.height + delta.y;
                    next.width = Mathf.Max(MinWidth * scale, next.width);
                    next.height = Mathf.Max(MinHeight * scale, next.height);
                    if ((resizeEdges & 1) != 0 && next.x + next.width < dragStartRect.xMax)
                        next.x = dragStartRect.xMax - next.width;
                    if ((resizeEdges & 4) != 0 && next.y + next.height < dragStartRect.yMax)
                        next.y = dragStartRect.yMax - next.height;
                }
                windowRect = Clamp(next);
            }
            if (Input.GetMouseButtonUp(0) && (resizeEdges != 0 || draggingWindow))
            { resizeEdges = 0; draggingWindow = false; SaveGeometry(); }
            hoverEdges = resizeEdges != 0 ? resizeEdges : NearEdges(rect, mouse, hit);
        }

        // Bit mask: 1 left, 2 right, 4 top, 8 bottom.
        private static int NearEdges(Rect rect, Vector2 point, float hit)
        {
            if (!new Rect(rect.x - hit, rect.y - hit, rect.width + hit * 2f, rect.height + hit * 2f).Contains(point)) return 0;
            int edges = 0;
            if (Mathf.Abs(point.x - rect.xMin) <= hit) edges |= 1;
            if (Mathf.Abs(point.x - rect.xMax) <= hit) edges |= 2;
            if (Mathf.Abs(point.y - rect.yMin) <= hit) edges |= 4;
            if (Mathf.Abs(point.y - rect.yMax) <= hit) edges |= 8;
            return edges;
        }
        private static Rect Lerp(Rect a, Rect b, float t)
        {
            return new Rect(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t),
                Mathf.Lerp(a.width, b.width, t), Mathf.Lerp(a.height, b.height, t));
        }

        private void DrawPanel()
        {
            if (animating) progress += Time.unscaledDeltaTime / ExpandDuration;
            else PollWindowPointer();
            if (progress >= 1f)
            {
                progress = 1f;
                animating = false;
                if (closing) { closing = false; open = false; return; }
            }
            shownRect = animating ? Lerp(animFrom, animTo, EaseOutCubic(Mathf.Clamp01(progress))) : ClampWindow();
            windowRect = shownRect;
            float fade = 1f;
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            if (animating)
            {
                fade = closing ? 1f - Mathf.Clamp01(Mathf.InverseLerp(.25f, .82f, progress))
                    : Mathf.Clamp01(Mathf.InverseLerp(.12f, .68f, progress));
                GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * fade);
                GUI.enabled = false;
            }
            try
            {
                EnsureBlocker();
                GUI.DrawTexture(shownRect, style.Panel, ScaleMode.StretchToFill, true);
                DrawFrame(shownRect);
                Rect used = GUILayout.Window(0x4C4F4D10, shownRect, DrawContents, "");
                if (!animating) { windowRect = Clamp(used); shownRect = windowRect; }
                else if (used != shownRect) windowRect = Clamp(used);
            }
            finally { GUI.color = previousColor; GUI.enabled = previousEnabled; }
            DrawEdgeHint(shownRect);
            DrawResizeGrip(shownRect);
        }

        // Active-edge highlight, matching the previous panel's resize affordance.
        private void DrawEdgeHint(Rect rect)
        {
            if (hoverEdges == 0 || Event.current.type != EventType.Repaint) return;
            float thickness = Mathf.Max(2f, 2f * scale);
            if ((hoverEdges & 1) != 0) GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), style.Accent);
            if ((hoverEdges & 2) != 0) GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), style.Accent);
            if ((hoverEdges & 4) != 0) GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), style.Accent);
            if ((hoverEdges & 8) != 0) GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), style.Accent);
        }

        private void DrawFrame(Rect rect)
        {
            float w = rect.width, h = rect.height;
            GUI.DrawTexture(new Rect(rect.x, rect.y, w, 1f), style.Border);
            GUI.DrawTexture(new Rect(rect.x, rect.y + h - 1f, w, 1f), style.Border);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, h), style.Border);
            GUI.DrawTexture(new Rect(rect.x + w - 1f, rect.y, 1f, h), style.Border);
            float c = 14f * scale;
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, c, 1f), style.AccentSoft);
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, 1f, c), style.AccentSoft);
            GUI.DrawTexture(new Rect(rect.x + w - 2f - c, rect.y + 2f, c, 1f), style.AccentSoft);
            GUI.DrawTexture(new Rect(rect.x + w - 3f, rect.y + 2f, 1f, c), style.AccentSoft);
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + h - 3f, c, 1f), style.AccentSoft);
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + h - 2f - c, 1f, c), style.AccentSoft);
            GUI.DrawTexture(new Rect(rect.x + w - 2f - c, rect.y + h - 3f, c, 1f), style.AccentSoft);
            GUI.DrawTexture(new Rect(rect.x + w - 3f, rect.y + h - 2f - c, 1f, c), style.AccentSoft);
            if (style.Ink != null)
                GUI.DrawTexture(new Rect(rect.x + Mathf.Max(0, w - 238f * scale), rect.y + Mathf.Max(70f, h - 205f * scale),
                    230f * scale, 175f * scale), style.Ink, ScaleMode.StretchToFill, true);
            if (w > 90f * scale && h > 50f * scale)
            {
                float rollW = 10f * scale, rollH = 18f * scale;
                GUI.DrawTexture(new Rect(rect.x + 4f * scale, rect.y + h - rollH + 2f * scale, rollW, rollH),
                    style.ScrollRoll, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(new Rect(rect.x + w - 14f * scale, rect.y + h - rollH + 2f * scale, rollW, rollH),
                    style.ScrollRoll, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(new Rect(rect.x + 12f * scale, rect.y + h - 2f * scale, w - 24f * scale, 2f * scale),
                    style.AccentSoft, ScaleMode.StretchToFill, true);
            }
        }

        private void DrawContents(int windowID)
        {
            float s = scale;
            float headerHeight = 42f * s;
            GUI.Label(new Rect(0, 0, shownRect.width, headerHeight), "活侠·万象录", style.Title);
            if (GUI.Button(new Rect(shownRect.width - 40f * s, 4f * s, 32f * s, 30f * s), "×", style.IconButton))
                Visible = false;
            GUILayout.Space(headerHeight);
            DrawTabs();
            GUILayout.Space(7f * s);

            bool features = plugin.Options.Enabled.Value;
            bool next = DrawSwitchRow("万象录功能总开关", features,
                "关闭后暂停万象录的辅助功能，保留各项设置；已写入的角色数值不会回退。");
            if (next != features) plugin.Options.Enabled.Value = features = next;
            if (!string.IsNullOrEmpty(plugin.Status))
            {
                GUILayout.BeginVertical(style.Notice);
                GUILayout.Label("上次操作：" + plugin.Status, style.Description);
                GUILayout.EndVertical();
                GUILayout.Space(7f * s);
            }
            if (!features) GUILayout.Label("万象录辅助功能已暂停；已接入的功能不生效。", style.Description);
            scroll[page] = GUILayout.BeginScrollView(scroll[page], false, true, GUILayout.ExpandHeight(true));
            if (features || page == 4)
            {
                switch (page)
                {
                    case 0: StoryPage(); break;
                    case 1: BattlePage(); break;
                    case 2: GrowthPage(); break;
                    case 3: NumericPage(); break;
                    default: SavePage(); break;
                }
            }
            GUILayout.EndScrollView();
            DrawBottomBar();
        }

        private void DrawTabs()
        {
            float s = scale;
            float rowHeight = 34f * s;
            if (tabTransition < 1f) tabTransition = Mathf.MoveTowards(tabTransition, 1f, Time.unscaledDeltaTime / .22f);
            int columns = shownRect.width < 480f * s ? 3 : Pages.Length;
            int rows = (Pages.Length + columns - 1) / columns;
            for (int row = 0; row < rows; row++)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
                for (int column = 0; column < columns; column++)
                {
                    int i = row * columns + column;
                    if (i >= Pages.Length) { GUILayout.FlexibleSpace(); break; }
                    Rect r = GUILayoutUtility.GetRect(GUIContent.none, style.Tab, GUILayout.MinWidth(0f),
                        GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
                    bool hover = r.Contains(Event.current.mousePosition);
                    if (i == page)
                    {
                        float t = EaseOutCubic(Mathf.Clamp01(tabTransition));
                        float paperWidth = Mathf.Lerp(12f * s, Mathf.Max(16f, r.width - 8f * s), t);
                        Rect paper = new Rect(r.center.x - paperWidth / 2f, r.y + 3f * s, paperWidth, r.height - 6f * s);
                        GUI.DrawTexture(paper, style.ScrollPaper, ScaleMode.StretchToFill, true);
                        float rollW = Mathf.Min(10f * s, paper.width * .18f);
                        GUI.DrawTexture(new Rect(paper.x - rollW * .35f, paper.y, rollW, paper.height),
                            style.ScrollRoll, ScaleMode.StretchToFill, true);
                        GUI.DrawTexture(new Rect(paper.xMax - rollW * .65f, paper.y, rollW, paper.height),
                            style.ScrollRoll, ScaleMode.StretchToFill, true);
                        GUI.DrawTexture(new Rect(paper.x, paper.y, paper.width, 1f), style.Accent);
                        GUI.DrawTexture(new Rect(paper.x, paper.yMax - 1f, paper.width, 1f), style.Accent);
                        var active = new GUIStyle(style.Tab);
                        active.normal.textColor = new Color(.17f, .13f, .09f, Mathf.Clamp01(t * 1.6f));
                        GUI.Label(r, Pages[i], active);
                    }
                    else
                    {
                        GUI.Label(r, Pages[i], hover ? style.TabHover : style.Tab);
                        if (hover) GUI.DrawTexture(new Rect(r.x + 8f * s, r.yMax - 2f * s, r.width - 16f * s, 1f), style.AccentSoft);
                    }
                    if (GUI.Button(r, GUIContent.none, style.TabHit) && i != page)
                    { page = i; tabTransition = 0f; GUI.FocusControl(null); }
                }
                GUILayout.EndHorizontal();
            }
            Rect rule = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(Mathf.Max(1f, s)));
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(rule, style.AccentSoft);
        }

        private void StoryPage()
        {
            int view = GUILayout.Toolbar(storyView, StoryViews, style.SubTab);
            if (view != storyView) { storyView = view; scroll[0] = Vector2.zero; }
            GUILayout.Space(8f * scale);
            if (storyView == 1) { Recall(); return; }
            Section("天命控制");
            plugin.Options.Mode.Value = GUILayout.Toolbar(plugin.Options.Mode.Value,
                new[] { "自然掷骰", "指定点数", "直接选项" }, style.SubTab);
            Note("自然掷骰不覆盖任何结果；指定点数按每次判定范围校验；直接选项在判定页点击目标选项。");
            bool pointMode = plugin.Options.Mode.Value == 1;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && pointMode;
            GUILayout.BeginHorizontal();
            GUILayout.Label("基础点数", style.Entry, GUILayout.Width(80f * scale));
            plugin.Options.Point.Value = GUILayout.TextField(plugin.Options.Point.Value, 8);
            if (GUILayout.Button("清空", style.SubtleStyle, GUILayout.Width(70f * scale))) plugin.Options.Point.Value = "";
            GUILayout.EndHorizontal();
            GUI.enabled = previousEnabled;
            Note(pointMode ? "清空点数恢复随机。重掷仍使用原生命运点。" : "切换到「指定点数」后此项才生效。");
            Section("选项提示");
            plugin.Options.Hints.Value = DrawSwitchRow("选项提示（含剧透）", plugin.Options.Hints.Value,
                "首次安装默认关闭；关闭总开关后保留两个子项的选择。收起面板后悬停选项生效。");
            plugin.Options.ShowEffects.Value = DrawSwitchRow("显示效果", plugin.Options.ShowEffects.Value);
            plugin.Options.ShowConditions.Value = DrawSwitchRow("显示条件", plugin.Options.ShowConditions.Value);
            Note("当前可对应的分支：" + HintAdapter.Count);
            Section("分支记录");
            plugin.Options.ShowBranches.Value = DrawSwitchRow("显示已走分支标记", plugin.Options.ShowBranches.Value,
                "选项会显示〔已走〕或〔未记录〕；点击时记录，保存在配置目录，不写入存档。");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("清空记录", style.SubtleStyle, GUILayout.Width(90f * scale)))
            { BranchHistory.Clear(); plugin.Status = "分支记录已清空。"; }
            GUILayout.Label("最近记录：", style.Description);
            GUILayout.EndHorizontal();
            var recent = BranchHistory.Recent(12);
            if (recent.Count == 0) Note("还没有记录；开启标记后正常做选择即可累积。");
            else foreach (string line in recent) GUILayout.Label("· " + line, style.Description);
            Section("精确恢复（实验）");
            GUILayout.BeginHorizontal();
            if (!StoryRecovery.Recording)
            {
                if (GUILayout.Button("开始记录", style.ButtonStyle, GUILayout.Width(90f * scale))) StoryRecovery.Begin();
            }
            else
            {
                if (GUILayout.Button("保存检查点", style.ButtonStyle, GUILayout.Width(110f * scale))) StoryRecovery.Save(recoverySlot);
                if (GUILayout.Button("停止记录", style.SubtleStyle, GUILayout.Width(90f * scale))) StoryRecovery.Stop();
            }
            recoverySlot = Mathf.Clamp(recoverySlot, 1, 9);
            GUILayout.Label("槽位", style.Description, GUILayout.Width(40f * scale));
            if (GUILayout.Button("－", style.SubtleStyle, GUILayout.Width(28f * scale))) recoverySlot--;
            GUILayout.Label(recoverySlot.ToString(), style.Entry, GUILayout.Width(24f * scale));
            if (GUILayout.Button("＋", style.SubtleStyle, GUILayout.Width(28f * scale))) recoverySlot++;
            GUILayout.EndHorizontal();
            Note("记录中：" + (StoryRecovery.Recording ? "是" : "否") + " · 已记录事件 " + StoryRecovery.Count + " 个");
            Note(StoryRecovery.Status);
            Note("本阶段只做记录：基线是当前存档的 JSON，事件是骰子结果与选项点击；文件写到配置目录 mhr\\slot-00N.mhr。");
            Note("播放尚未实现——恢复需要把基线写回游戏再重放事件，必须实机逐步验证后才开放，避免把进度改坏。");
            Section("高级工具（诊断与强制事件）");
            plugin.Options.CheckpointLog.Value = DrawSwitchRow("检查点更新提醒（剧情标记，不是存档点）", plugin.Options.CheckpointLog.Value,
                "剧情标记变化时在「上次操作」提示名称与值。");
            Unported("强制事件 / 取消待执行事件与任务", "跳过前置条件会直接影响剧情走向，需实机验证后再开放。");
            Note("开发状态：主面板、骰子控制、受限提示、属性/好感/物品/速度、单挑与群战辅助已接入；恢复适配未完成。");
        }

        private void Recall()
        {
            if (DuelTools.Dialogue.Count == 0) { Note("本场还没有记录。回想最多保留 500 条，不暂停战斗。"); return; }
            foreach (string line in DuelTools.Dialogue) GUILayout.Label(line, style.Entry);
            Note("回想最多保留 500 条；新单挑开始时清空。");
        }

        private void BattlePage()
        {
            Section("单挑辅助");
            plugin.Options.DuelHealth.Value = DrawSwitchRow("主角生命不减少", plugin.Options.DuelHealth.Value,
                "阻止战斗扣血和持续伤害；保留剧情直接改值及强制胜负。");
            plugin.Options.DuelStamina.Value = DrawSwitchRow("单挑无限气力", plugin.Options.DuelStamina.Value,
                "保持单挑行动所需气力，不修改周目内力属性。");
            plugin.Options.DuelDodge.Value = DrawSwitchRow("主角最高闪避率", plugin.Options.DuelDodge.Value,
                "提高原生闪避率，不能避开必中特技或剧情结果。");
            plugin.Options.DuelDamage.Value = DrawSwitchRow("敌方受到有效伤害时一击必杀", plugin.Options.DuelDamage.Value,
                "保留剧情最低生命保护与强制胜负。仅单挑生效；启用或战中切换后本场不能再保存单挑回合。");
            Section("群战辅助");
            plugin.Options.GroupHealth.Value = DrawSwitchRow("主角生命不减少", plugin.Options.GroupHealth.Value,
                "仍可能硬直；友军生命和剧情强制退场沿用原生规则。");
            plugin.Options.GroupMana.Value = DrawSwitchRow("主角气力保持充足", plugin.Options.GroupMana.Value,
                "作用于群战气力条。");
            plugin.Options.GroupCooldown.Value = DrawSwitchRow("群战技能栏立即冷却", plugin.Options.GroupCooldown.Value,
                "保留原生施放限制与演出。");
            plugin.Options.GroupFreeze.Value = DrawSwitchRow("冻结群战剩余时间", plugin.Options.GroupFreeze.Value,
                "原生倒计时延长，增援与事件照常推进；按计时归零获胜的关卡请关闭。");
            plugin.Options.GroupDown.Value = DrawSwitchRow("敌方受伤即倒地", plugin.Options.GroupDown.Value,
                "含友军攻击和持续伤害；保留敌方闪避与剧情无敌，不影响中立及友方。");
        }

        private void GrowthPage()
        {
            int view = GUILayout.Toolbar(growthView, GrowthViews, style.SubTab);
            if (view != growthView) { growthView = view; scroll[2] = Vector2.zero; }
            GUILayout.Space(8f * scale);
            if (growthView == 1) { Relationships(); return; }
            Note("编辑已登场角色的好感度与心上人，正常存档后保留。");
            Section("练功");
            plugin.Options.SkillFree.Value = DrawSwitchRow("练功消耗不减", plugin.Options.SkillFree.Value,
                "练功结束后把武学点数恢复到操作前的值；武功修炼条件仍须满足。");
            plugin.Options.FastSkill.Value = DrawSwitchRow("快速练功", plugin.Options.FastSkill.Value,
                "把本次消耗替换为当前级剩余点数，一次完成一级；仍执行原生升级奖励与条件。");
            Section("锻造与炼丹");
            plugin.Options.ForgeFree.Value = DrawSwitchRow("锻造炼丹消耗不减", plugin.Options.ForgeFree.Value,
                "操作结束后把点数与金钱恢复到操作前的值；不解锁研发条件。");
            plugin.Options.FastForge.Value = DrawSwitchRow("快速锻造炼丹", plugin.Options.FastForge.Value,
                "把本次消耗替换为当前级剩余点数，一次完成一级；保留原生等级上限与奖励。");
            Section("日常行动");
            plugin.Options.UnlimitedActions.Value = DrawSwitchRow("无限日常行动点", plugin.Options.UnlimitedActions.Value,
                "行动点至少为 1 且不减，日期不会因行动用完自动前进。");
            Section("最高好感");
            plugin.Options.MaxAffection.Value = DrawSwitchRow("已登场角色保持各自好感上限", plugin.Options.MaxAffection.Value,
                "不解锁未登场角色；正常存档后保留，关闭不会还原。");
            Note("以上四项在自由活动场景空闲时生效。练功与锻造炼丹两组尚未接入。");
        }

        private void Relationships()
        {
            Section("指定角色好感度");
            if (!GameAccess.CanEdit)
                Note("请在周目剧情或自由行动中编辑；先关闭商店、升级等面板，并等待加载完成。");
            else
            {
                var list = PlayerStatManagerData.Instance.Relationships.List;
                foreach (var item in list)
                {
                    if (item == null || !item.Active) continue;
                    if (GUILayout.Button((relationship == item ? "● " : "") + item.Type + "   " + item.Value, style.ButtonStyle))
                    { relationship = item; expectedRelationship = item.Value; relationshipValue = item.Value.ToString(); }
                }
                if (relationship != null)
                {
                    int maximum = GameAccess.Field<int>(relationship, "_max");
                    Note("修改 " + relationship.Type + " 好感：0～" + maximum + "；仅列出已登场角色。");
                    relationshipValue = GUILayout.TextField(relationshipValue, 12);
                    if (GUILayout.Button("应用好感度", style.ButtonStyle))
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
                Note("修改当前周目，正常存档后保留。不会补齐恋爱事件、结缘条件或解锁未登场角色。");
            }
            Section("心上人");
            loverIndex = GUILayout.SelectionGrid(loverIndex, Lovers, 5, style.SubTab);
            if (GUILayout.Button("应用心上人", style.ButtonStyle))
                plugin.Status = ProgressionTools.SetLover(loverIndex);
            Note("可选择九位候选之一或「自己」。不补齐结缘事件、不清除已发生的恋爱剧情标记。此功能尚未实机验证。");
        }

        private void NumericPage()
        {
            Section("属性");
            StatsBody();
            Section("物品数量");
            ItemsBody();
            Section("游戏速度");
            GUILayout.BeginHorizontal();
            float speed = GUILayout.HorizontalSlider(plugin.Options.Speed.Value, 0.25f, 5f);
            GUILayout.Label(speed.ToString("0.00") + " 倍", style.FooterValue, GUILayout.Width(70f * scale));
            if (GUILayout.Button("恢复 1 倍", style.SubtleStyle, GUILayout.Width(90f * scale))) plugin.ApplySpeed(1f);
            else if (Mathf.Abs(speed - plugin.Options.Speed.Value) > 0.02f) plugin.ApplySpeed(speed);
            GUILayout.EndHorizontal();
            Note("只改引擎时间倍率；原生暂停、慢动作与快进各有自己的状态。");
            Section("其他");
            plugin.Options.PracticeLimit.Value = DrawSwitchRow("解除修炼点数消耗上限", plugin.Options.PracticeLimit.Value,
                "练功面板不再限制单次消耗，按现有武学点数扣减；研究条件与升级奖励不变。");
        }

        private void StatsBody()
        {
            bool safe = GameAccess.CanEdit && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Free";
            if (!safe) { Note("修改会影响进度，请使用备份周目。请先进入自由活动场景，关闭游戏内交互面板。"); selected = null; return; }
            var player = PlayerStatManagerData.Instance;
            GUILayout.BeginHorizontal();
            GUILayout.Label("筛选", style.Description, GUILayout.Width(45f * scale));
            search = GUILayout.TextField(search, 80);
            GUILayout.EndHorizontal();
            foreach (var stat in player.Stats.List)
            {
                if (stat == null || !Editable.Contains(stat.StatType.ToString())) continue;
                string name = DisplayName(stat);
                if (!string.IsNullOrEmpty(search) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button((selected == stat ? "● " : "") + name + "   " + stat.Value + " / 最终 " + stat.FinalValue, style.SubtleStyle))
                { selected = stat; expectedValue = stat.Value; value = expectedValue.ToString(); }
            }
            if (selected == null) return;
            GUILayout.Space(8f * scale);
            Note(DisplayName(selected) + "：允许 " + selected.Min + "～" + selected.Max);
            value = GUILayout.TextField(value, 12);
            if (GUILayout.Button("应用到所选属性", style.ButtonStyle))
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
            if (!GameAccess.CanEdit) { Note("请先进入自由活动场景并关闭游戏内交互面板。"); selectedItem = null; return; }
            var database = ItemDatabase.Instance;
            if (database == null) { Note("物品数据库尚未就绪。"); return; }
            // Same entry path as before: select the item in the game's inventory
            // panel, then edit exactly that item here.
            var picked = ItemSelection.Selected;
            if (picked != null)
            {
                if (GUILayout.Button("编辑游戏内选中的物品：" + ItemName(picked) + "   " + picked.CurrentCount, style.ButtonStyle))
                { selectedItem = picked; expectedItemCount = picked.CurrentCount; itemValue = expectedItemCount.ToString(); }
            }
            else Note("也可以先在游戏物品面板选中一个物品，这里会直接取该物品。");
            int tab = GUILayout.Toolbar((int)itemType - 1, new[] { "書籍", "雜物", "貴重品", "消耗品" }, style.SubTab);
            itemType = (GameItemType)(tab + 1);
            showAllItems = DrawSwitchRow("也列出数量为 0 的物品", showAllItems, "勾选后可以给未持有的物品直接设置数量，等同于生成。");
            GUILayout.BeginHorizontal();
            GUILayout.Label("筛选", style.Description, GUILayout.Width(45f * scale));
            itemSearch = GUILayout.TextField(itemSearch, 80);
            GUILayout.EndHorizontal();
            ItemDataCollection collection = tab == 0 ? database.Books : tab == 1 ? database.Miscs
                : tab == 2 ? database.Special : database.Consumables;
            if (collection == null || collection.List == null) { Note("该分类物品列表尚未就绪。"); return; }
            foreach (var item in collection.List)
            {
                if (item == null || (!showAllItems && item.CurrentCount <= 0)) continue;
                string label = ItemName(item) + "   " + item.CurrentCount;
                if (!string.IsNullOrEmpty(itemSearch) && label.IndexOf(itemSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button((selectedItem == item ? "● " : "") + label, style.SubtleStyle))
                { selectedItem = item; expectedItemCount = item.CurrentCount; itemValue = expectedItemCount.ToString(); }
            }
            if (selectedItem == null) return;
            GUILayout.Space(8f * scale);
            Note(ItemName(selectedItem) + "：当前 " + selectedItem.CurrentCount + "，可设为 0～999999");
            itemValue = GUILayout.TextField(itemValue, 12);
            if (GUILayout.Button("应用到所选物品", style.ButtonStyle))
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
            GUILayout.Label("允许范围 2～999", style.Description, GUILayout.Width(140f * scale));
            if (string.IsNullOrEmpty(slotCountText)) slotCountText = plugin.Options.SlotCount.Value.ToString();
            slotCountText = GUILayout.TextField(slotCountText, 4, GUILayout.Width(60f * scale));
            if (GUILayout.Button("保存格数", style.ButtonStyle, GUILayout.Width(90f * scale)))
            {
                int parsed;
                if (!int.TryParse(slotCountText, out parsed) || parsed < 2 || parsed > 999)
                    plugin.Status = "格数必须是 2～999 的整数。";
                else
                {
                    plugin.Options.SlotCount.Value = parsed;
                    plugin.Options.Config.Save();
                    plugin.Status = "格数已保存为 " + parsed + "；重新打开存档或读档界面后生效。";
                }
            }
            if (GUILayout.Button("恢复默认", style.SubtleStyle, GUILayout.Width(90f * scale)))
            {
                plugin.Options.SlotCount.Value = 0; slotCountText = "0";
                plugin.Options.Config.Save();
                plugin.Status = "已恢复游戏默认格数。";
            }
            GUILayout.EndHorizontal();
            Note("只改变列表里建多少个槽位，不改动已有存档文件；填 0 或点「恢复默认」使用游戏原值。");
            Section("存档备注");
            string selected = SaveSlotComments.Selected;
            if (string.IsNullOrEmpty(selected)) Note("先在游戏存档列表选中一个槽位，再按 F2 打开这里。");
            else
            {
                if (commentSlot != selected) { commentSlot = selected; slotComment = SaveSlotComments.Get(selected); }
                Note("槽位 " + selected + " 的备注（保存在配置目录，不写入存档）");
                slotComment = GUILayout.TextField(slotComment, 60);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("保存备注", style.ButtonStyle, GUILayout.Width(100f * scale)))
                { SaveSlotComments.Set(selected, slotComment); plugin.Status = "备注已保存。"; }
                if (GUILayout.Button("清除备注", style.SubtleStyle, GUILayout.Width(100f * scale)))
                { SaveSlotComments.Set(selected, ""); slotComment = ""; plugin.Status = "备注已清除。"; }
                GUILayout.EndHorizontal();
                Note("备注会追加显示在该槽位的标题下方，形如〔备注〕…。");
            }
            Section("排序");
            plugin.Options.SortRecent.Value = DrawSwitchRow("按最近存档排序（F3 切换）", plugin.Options.SortRecent.Value,
                "只在读档列表里按存档时间重排行顺序，槽位编号与存档文件不变。");
            Note("存读仍使用游戏原生系统菜单与编号槽。本页为占位，旧版对应功能尚未重新实现。");
        }

        private void DrawBottomBar()
        {
            float s = scale;
            GUILayout.Space(6f * s);
            Rule();
            GUILayout.BeginHorizontal(GUILayout.Height(42f * s));
            GUILayout.Label("界面透明度", style.FooterLabel, GUILayout.Width(76f * s));
            float sliderWidth = Mathf.Clamp(shownRect.width - 262f * s, 60f * s, 300f * s);
            Rect sliderRect = GUILayoutUtility.GetRect(sliderWidth, 30f * s, GUILayout.Width(sliderWidth), GUILayout.Height(30f * s));
            float normalized = Mathf.InverseLerp(.4f, 1f, opacity);
            float trackY = sliderRect.y + sliderRect.height * .5f - 3f * s;
            Rect track = new Rect(sliderRect.x + 8f * s, trackY, sliderRect.width - 16f * s, 6f * s);
            GUI.DrawTexture(track, style.SliderTrack, ScaleMode.StretchToFill, true);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * normalized, track.height),
                style.SliderFill, ScaleMode.StretchToFill, true);
            float next = GUI.HorizontalSlider(sliderRect, opacity, .4f, 1f, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (Mathf.Abs(next - opacity) > .0001f)
            {
                opacity = Mathf.Clamp(next, .4f, 1f);
                style.SetOpacity(opacity);
                plugin.Options.Opacity.Value = opacity;
            }
            GUILayout.Label(Mathf.RoundToInt(opacity * 100f) + "%", style.FooterValue, GUILayout.Width(48f * s));
            GUILayout.FlexibleSpace();
            GUILayout.Label(plugin.Options.Toggle.Value + " 收起", style.FooterLabel, GUILayout.Width(72f * s));
            GUILayout.EndHorizontal();
        }

        private void DrawResizeGrip(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            float s = scale;
            float x = rect.xMax, y = rect.yMax;
            for (int i = 0; i < 3; i++)
            {
                float inset = (6f + i * 5f) * s;
                float length = (5f + i * 3f) * s;
                GUI.DrawTexture(new Rect(x - inset - length, y - inset, Mathf.Max(2f, 2f * s), Mathf.Max(2f, 2f * s)), style.AccentSoft);
                GUI.DrawTexture(new Rect(x - inset, y - inset - length, Mathf.Max(2f, 2f * s), Mathf.Max(2f, 2f * s)), style.AccentSoft);
            }
            Vector2 mouse = Event.current.mousePosition;
            Rect grip = new Rect(x - 26f * s, y - 26f * s, 26f * s, 26f * s);
            if (Event.current.type == EventType.MouseDown && grip.Contains(mouse))
            { resizing = true; dragStart = GUIUtility.GUIToScreenPoint(mouse); sizeStart = new Vector2(windowRect.width, windowRect.height); Event.current.Use(); }
            if (resizing && Event.current.type == EventType.MouseDrag)
            {
                Vector2 delta = GUIUtility.GUIToScreenPoint(Event.current.mousePosition) - dragStart;
                windowRect.width = Mathf.Max(MinWidth * scale, sizeStart.x + delta.x);
                windowRect.height = Mathf.Max(MinHeight * scale, sizeStart.y + delta.y);
                Event.current.Use();
            }
            if (resizing && Event.current.type == EventType.MouseUp)
            { resizing = false; SaveGeometry(); Event.current.Use(); }
        }

        private void DrawBadge()
        {
            Rect badge = BadgeRect();
            if (!loggedBadge) { loggedBadge = true; plugin.Log("Badge: " + Describe()); }
            bool hover = badge.Contains(Event.current.mousePosition);
            GUI.DrawTexture(badge, draggingBadge ? style.BadgeActive : hover ? style.BadgeHover : style.Badge, ScaleMode.StretchToFill, true);
            float s = scale;
            float line = Mathf.Max(1f, s);
            Texture2D edge = hover || draggingBadge ? style.Accent : style.Border;
            GUI.DrawTexture(new Rect(badge.x, badge.y, badge.width, line), edge);
            GUI.DrawTexture(new Rect(badge.x, badge.yMax - line, badge.width, line), edge);
            GUI.DrawTexture(new Rect(badge.x, badge.y, line, badge.height), edge);
            GUI.DrawTexture(new Rect(badge.xMax - line, badge.y, line, badge.height), edge);
            float inset = Mathf.Min(5f * s, badge.height * .06f);
            float rollH = Mathf.Min(8f * s, badge.height * .10f);
            float rollW = Mathf.Max(1f, badge.width - 10f * s);
            float rollX = badge.x + (badge.width - rollW) * .5f;
            GUI.DrawTexture(new Rect(rollX, badge.y + inset, rollW, rollH), style.ScrollRoll, ScaleMode.StretchToFill, true);
            GUI.DrawTexture(new Rect(rollX, badge.yMax - inset - rollH, rollW, rollH), style.ScrollRoll, ScaleMode.StretchToFill, true);
            float footerH = Mathf.Min(16f * s, badge.height * .14f);
            float footerY = badge.yMax - inset - rollH - footerH - 5f * s;
            float titleY = badge.y + inset + rollH + 8f * s;
            float titleH = Mathf.Max(1f, footerY - titleY - 6f * s);
            float sideInset = Mathf.Min(7f * s, badge.width * .18f);
            GUI.DrawTexture(new Rect(badge.x + sideInset, titleY, line, titleH), style.AccentSoft);
            GUI.DrawTexture(new Rect(badge.xMax - sideInset - line, titleY, line, titleH), style.AccentSoft);
            var title = new GUIStyle(GUI.skin.label)
            { font = style.BadgeFont ?? style.UiFont, fontSize = Mathf.Max(16, (int)(18 * s)), alignment = TextAnchor.MiddleCenter, wordWrap = false };
            title.normal.textColor = new Color(.92f, .88f, .79f, 1f);
            GUI.Label(new Rect(badge.x + 3f * s, titleY, badge.width - 6f * s, titleH), "萬\n象\n錄", title);
            float tagWidth = Mathf.Max(1f, badge.width - 14f * s);
            Rect tagRect = new Rect(badge.x + (badge.width - tagWidth) * .5f, footerY, tagWidth, footerH);
            GUI.DrawTexture(new Rect(badge.x + 10f * s, footerY - 3f * s, Mathf.Max(1f, badge.width - 20f * s), line), style.AccentSoft);
            GUI.DrawTexture(tagRect, style.BambooTag, ScaleMode.StretchToFill, true);
            var key = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, (int)(12 * s)), alignment = TextAnchor.MiddleCenter };
            key.normal.textColor = new Color(.83f, .64f, .35f, 1f);
            GUI.Label(tagRect, plugin.Options.Toggle.Value.ToString(), key);

            Vector2 mouse = Event.current.mousePosition;
            if (Event.current.type == EventType.MouseDown && badge.Contains(mouse))
            { draggingBadge = true; badgeMoved = false; dragStart = GUIUtility.GUIToScreenPoint(mouse); Event.current.Use(); }
            if (draggingBadge && Event.current.type == EventType.MouseDrag)
            {
                Vector2 delta = GUIUtility.GUIToScreenPoint(Event.current.mousePosition) - dragStart;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) > 6f) badgeMoved = true;
                if (badgeMoved)
                {
                    badgePosition += delta; dragStart = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                    badgePosition.x = Mathf.Clamp(badgePosition.x, 8f, Mathf.Max(8f, Screen.width - badge.width - 8f));
                    badgePosition.y = Mathf.Clamp(badgePosition.y, 8f, Mathf.Max(8f, Screen.height - badge.height - 8f));
                }
                Event.current.Use();
            }
            if (draggingBadge && Event.current.type == EventType.MouseUp)
            {
                draggingBadge = false;
                SaveBadge();
                if (!badgeMoved) Visible = true;
                Event.current.Use();
            }
        }

        private void EnsureBlocker()
        {
            if (blocker != null) { blocker.SetActive(true); return; }
            blocker = new GameObject("MortalHelper.PointerShield", typeof(Canvas), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(blocker);
            var canvas = blocker.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
            var child = new GameObject("Block", typeof(RectTransform), typeof(Image));
            child.transform.SetParent(blocker.transform, false);
            var rect = (RectTransform)child.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            child.GetComponent<Image>().color = Color.clear;
        }

        private void Section(string title)
        {
            GUILayout.Space(5f * scale);
            GUILayout.BeginHorizontal(GUILayout.Height(26f * scale));
            GUILayout.Label("◇ " + title, style.SectionTitle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Rule();
            GUILayout.Space(4f * scale);
        }

        private void Rule()
        {
            Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
            GUI.DrawTexture(r, style.AccentSoft);
        }

        private bool DrawSwitchRow(string label, bool value, string hint = null)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(28f * scale));
            GUILayout.Label(label, style.Entry, GUILayout.ExpandWidth(true));
            bool next = GUILayout.Toggle(value, GUIContent.none, style.SwitchTrack,
                GUILayout.Width(52f * scale), GUILayout.Height(25f * scale));
            GUILayout.Label(next ? "开" : "关", style.SwitchState, GUILayout.Width(22f * scale));
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(hint)) GUILayout.Label(hint, style.Description);
            Rule();
            return next;
        }

        private void Unported(string label, string description)
        {
            bool previous = GUI.enabled;
            GUI.enabled = false;
            DrawSwitchRow(label, false);
            GUI.enabled = previous;
            Note(description);
        }

        private void Note(string text) { GUILayout.Label(text, style.Description); }

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
            return "open=" + open + " window=" + windowRect.x.ToString("0") + "," + windowRect.y.ToString("0") +
                " " + windowRect.width.ToString("0") + "x" + windowRect.height.ToString("0") +
                " badge=" + badgePosition.x.ToString("0") + "," + badgePosition.y.ToString("0") +
                " screen=" + Screen.width + "x" + Screen.height;
        }

        private void SaveGeometry()
        {
            var rect = ClampWindow();
            windowRect = rect;
            plugin.Options.X.Value = rect.x; plugin.Options.Y.Value = rect.y;
            plugin.Options.Width.Value = rect.width; plugin.Options.Height.Value = rect.height;
            plugin.Options.Config.Save();
        }

        private void SaveBadge()
        {
            plugin.Options.BadgeX.Value = badgePosition.x;
            plugin.Options.BadgeY.Value = badgePosition.y;
            plugin.Options.Config.Save();
        }

        internal void ClearSelection() { selected = null; relationship = null; selectedItem = null; }
        internal void Dispose()
        {
            if (blocker != null) UnityEngine.Object.Destroy(blocker);
            style.Dispose();
        }
    }
}
