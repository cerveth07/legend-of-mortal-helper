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
        private const float BadgeWidth = 58f;
        private const float BadgeHeight = 168f;
        private const float MinWidth = 420f;
        private const float MinHeight = 600f;

        private static readonly string[] Pages = { "剧情辅助", "战斗辅助", "养成辅助", "数值编辑", "存档设置" };
        private static readonly string[] StoryViews = { "剧情", "回想" };
        private static readonly string[] ItemViews = { "书籍", "杂物", "贵重品", "消耗品" };
        private static readonly string[] BattleViews = { "单挑", "群战" };
        private static readonly string[] GrowthViews = { "养成", "人际" };
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
        private Rect masterRect;
        private bool recoveryOpen;
        private bool editOpen = true;
        private bool confirmResetSlots, confirmClearComments;
        private bool confirmRestoreMain, confirmRestoreBackup;
        private readonly Dictionary<string, string> relationshipEdits = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> statEdits = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> itemEdits = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<EditRecord> editLog = new List<EditRecord>();
        private sealed class EditRecord { internal string Label; internal Action Undo; }
        private readonly System.Collections.Generic.List<string> marks = new System.Collections.Generic.List<string>();

        private Vector2 badgePosition;
        private bool open, initialized;
        private float scale;
        private float opacity;
        private bool animating, closing;
        private float progress;
        private Rect animFrom, animTo;
        private float tabTransition = 1f;
        private int page, storyView, battleView, growthView, loverIndex;
        private int lastPage = -1;
        private readonly Queue<Action> deferred = new Queue<Action>();
        private string searchEdit = "", itemSearchEdit = "";
        private readonly Vector2[] scroll = new Vector2[5];
        private GameItemType itemType = GameItemType.雜物;
        private string itemSearch = "", search = "";
        private bool showAllItems, loggedBadge;
        private string slotComment = "", commentSlot, slotCountText = "";
        private int recoverySlot = 1;
        private GameObject blocker;
        private RectTransform blockerRect;
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

        // Used by the layout probe so a page can be measured without input.
        internal void OpenProbePage(int index)
        {
            page = Mathf.Clamp(index, 0, 4);
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

        // Runs outside OnGUI: applying state changes here keeps every frame's
        // control count identical between the layout and repaint passes.
        internal bool RecoveryOpen { get { return recoveryOpen; } set { recoveryOpen = value; } }

        internal void SetView(int story, int battle, int growth)
        {
            storyView = Mathf.Clamp(story, 0, StoryViews.Length - 1);
            battleView = Mathf.Clamp(battle, 0, BattleViews.Length - 1);
            growthView = Mathf.Clamp(growth, 0, GrowthViews.Length - 1);
        }

        internal void ScrollToEnd(int target)
        {
            if (target < 0 || target >= scroll.Length) return;
            scroll[target] = new Vector2(0f, 100000f);
        }

        internal string Geo()
        {
            return "shown=" + shownRect.width.ToString("0.0") + "x" + shownRect.height.ToString("0.0")
                + " window=" + windowRect.width.ToString("0.0")
                + " content=" + ContentWidth().ToString("0.0")
                + " scale=" + scale.ToString("0.00");
        }

        internal int Page
        {
            get { return page; }
            set { page = Mathf.Clamp(value, 0, Pages.Length - 1); scroll[page] = Vector2.zero; lastPage = page; }
        }

        internal void Drain()
        {
            while (deferred.Count > 0)
            {
                try { deferred.Dequeue()(); }
                catch (Exception error) { plugin.Report(error); }
            }
            if (!string.Equals(search, searchEdit, StringComparison.Ordinal)) search = searchEdit;
            if (!string.Equals(itemSearch, itemSearchEdit, StringComparison.Ordinal)) itemSearch = itemSearchEdit;
        }

        private void Defer(Action action) { if (action != null) deferred.Enqueue(action); }

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
            float designScale = Mathf.Max(1f, scale);
            float minWidth = Mathf.Min(MinWidth * designScale, Mathf.Max(1f, Screen.width - 24f));
            float minHeight = Mathf.Min(MinHeight * designScale, Mathf.Max(1f, Screen.height - 24f));
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
                else if (new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 44f * scale), 42f * scale).Contains(mouse) && !InMasterSwitch(mouse))
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
                if (closing) { closing = false; open = false; HideBlocker(); SaveGeometry(); return; }
            }
            shownRect = animating ? Lerp(animFrom, animTo, EaseOutCubic(Mathf.Clamp01(progress))) : ClampWindow();
            // windowRect is the user geometry: only a settled frame may refresh it.
            if (!animating) windowRect = shownRect;
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
                EnsureBlocker(shownRect);
                GUI.DrawTexture(shownRect, style.Panel, ScaleMode.StretchToFill, true);
                DrawFrame(shownRect);
                Rect used = GUILayout.Window(0x4C4F4D10, shownRect, DrawContents, "");
                if (!animating) { windowRect = Clamp(used); shownRect = windowRect; }
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
            DrawMasterSwitch();
            GUI.DrawTexture(new Rect(16f * s, headerHeight - 1f, Mathf.Max(0f, shownRect.width - 32f * s), 1f), style.Hairline);
            GUILayout.Space(headerHeight);
            DrawTabs();
            GUILayout.Space(8f * s);

            bool features = plugin.Options.Enabled.Value;
            if (page != lastPage) { scroll[page] = Vector2.zero; lastPage = page; }

            // Wide windows keep one fixed content column, centred. Stretching rows was
            // the "wide panel looks empty" report; margins read as deliberate instead.
            float contentWidth = ContentWidth();
            float horizontalPadding = style.Skin != null ? style.Skin.window.padding.horizontal : 32f;
            float sideMargin = Mathf.Max(0f, (shownRect.width - horizontalPadding - contentWidth) * .5f);
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            if (sideMargin > .5f) GUILayout.Space(sideMargin);
            GUILayout.BeginVertical(GUILayout.Width(contentWidth), GUILayout.ExpandHeight(true));

            // Constant height status slot: appearing and vanishing used to shift every
            // control on the page up and down by a line.
            float statusHeight = style.Description.CalcHeight(new GUIContent("上次操作：测"), contentWidth) +
                                 style.Notice.padding.vertical + 6f * s;
            if (!string.IsNullOrEmpty(plugin.Status))
            {
                GUILayout.BeginVertical(style.Notice);
                Color before = GUI.color;
                GUI.color = StatusLooksBad(plugin.Status) ? new Color(1f, .80f, .74f) : new Color(.98f, .90f, .70f);
                GUILayout.Label("上次操作：" + plugin.Status, style.Description);
                GUI.color = before;
                GUILayout.EndVertical();
                GUILayout.Space(7f * s);
            }
            else
            {
                GUILayout.Space(statusHeight + 7f * s);
            }
            if (!features) GUILayout.Label("万象录辅助功能已暂停；已接入的功能不生效。", style.Description);
            // Only show the scrollbar when the page actually overflows; the always-on
            // track drew a gold hairline down the right edge of every page.
            scroll[page] = GUILayout.BeginScrollView(scroll[page], false, false, GUIStyle.none,
                GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));
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
            GUILayout.Space(16f * s);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            DrawBottomBar();
        }

        // The master switch lives in the title bar so every page keeps its full height.
        private void DrawMasterSwitch()
        {
            float s = scale;
            float gap = 8f * s;
            float track = 52f * s, label = 26f * s;
            float right = shownRect.width - 44f * s - gap;
            masterRect = new Rect(right - track - gap - label, 6f * s, track + gap + label, 30f * s);
            bool features = plugin.Options.Enabled.Value;
            bool next = GUI.Toggle(new Rect(masterRect.x, masterRect.y + 2.5f * s, track, 25f * s),
                features, GUIContent.none, style.SwitchTrack);
            GUI.Label(new Rect(masterRect.x + track + gap, masterRect.y + 2.5f * s, label, 25f * s),
                next ? "开" : "关", style.SwitchState);
            if (next != features)
            {
                plugin.Options.Enabled.Value = next;
                plugin.Options.Config.Save();
            }
        }

        // Screen-space hit test so a click on the switch does not start a window drag.
        private bool InMasterSwitch(Vector2 screenPoint)
        {
            if (masterRect.width <= 0f) return false;
            var rect = windowRect;
            return new Rect(rect.x + masterRect.x - 4f * scale, rect.y + masterRect.y,
                masterRect.width + 8f * scale, masterRect.height).Contains(screenPoint);
        }

        private void DrawTabs()
        {
            float s = scale;
            float rowHeight = 34f * s;
            if (tabTransition < 1f) tabTransition = Mathf.MoveTowards(tabTransition, 1f, Time.unscaledDeltaTime / .22f);
            float available = ContentWidth();
            float widest = 0f;
            foreach (string caption in Pages)
                widest = Mathf.Max(widest, style.Tab.CalcSize(new GUIContent(caption)).x + 20f * s);
            int columns = Pages.Length;
            while (columns > 1 && widest * columns > available) columns--;
            float slotWidth = available / Mathf.Max(1, columns);
            if (widest > slotWidth && style.Tab.fontSize > 10)
                style.Tab.fontSize = style.TabHover.fontSize = Mathf.Max(10, Mathf.FloorToInt(style.Tab.fontSize * slotWidth / widest));
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
                        if (tabActiveStyle == null || tabActiveScale != style.Scale)
                        {
                            tabActiveStyle = new GUIStyle(style.Tab);
                            tabActiveScale = style.Scale;
                        }
                        tabActiveStyle.normal.textColor = new Color(.17f, .13f, .09f, Mathf.Clamp01(t * 1.6f));
                        GUI.Label(r, Pages[i], tabActiveStyle);
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
            int view = ViewTabs(storyView, StoryViews);
            if (view != storyView) { storyView = view; scroll[0] = Vector2.zero; }
            GUILayout.Space(8f * scale);
            if (storyView == 1) { Recall(); return; }

            Section("选项提示");
            plugin.Options.Hints.Value = DrawSwitchRow("选项提示（含剧透）", plugin.Options.Hints.Value,
                "收起面板后悬停选项生效；关闭总开关不会清掉下面两项的选择。");
            plugin.Options.ShowEffects.Value = DrawSwitchRow("显示效果", plugin.Options.ShowEffects.Value,
                "列出脚本里能确定的数值/旗标变化。");
            plugin.Options.ShowConditions.Value = DrawSwitchRow("显示条件", plugin.Options.ShowConditions.Value,
                "列出游戏原生的选择条件文本。");
            plugin.Options.CheckpointLog.Value = DrawSwitchRow("检查点更新提醒", plugin.Options.CheckpointLog.Value,
                "剧情标记变化时在「上次操作」提示名称与值；这是剧情标记，不是存档点。");
            if (HintAdapter.Count > 0) Note("本段可对应的分支：" + HintAdapter.Count);

            Section("分支记录");
            plugin.Options.ShowBranches.Value = DrawSwitchRow("显示已走分支标记", plugin.Options.ShowBranches.Value,
                "选项显示〔已走〕或〔未记录〕；只保存在配置目录，不写入存档。");
            var recent = BranchHistory.Recent(12);
            GUILayout.BeginHorizontal();
            GUILayout.Label(recent.Count == 0 ? "最近记录：还没有记录" : "最近记录", style.Description,
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button("清空", style.SubtleStyle, GUILayout.Width(64f * scale), GUILayout.Height(RowH)))
            { BranchHistory.Clear(); plugin.Status = "分支记录已清空。"; }
            GUILayout.EndHorizontal();
            foreach (string line in recent) GUILayout.Label("· " + line, style.Description);

            recoveryOpen = Fold("精确恢复（实验）", recoveryOpen, () => recoveryOpen = !recoveryOpen);
            if (!recoveryOpen) return;
            Note("记录中：" + (StoryRecovery.Recording ? "是" : "否") + " · 事件 " + StoryRecovery.Count
                + " 个 · 槽位 " + recoverySlot);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(StoryRecovery.Recording ? "停止记录" : "开始记录", style.ButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH)))
            {
                if (StoryRecovery.Recording) Defer(() => StoryRecovery.Stop());
                else Defer(() => StoryRecovery.Begin());
            }
            if (GUILayout.Button("保存检查点", style.SubtleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH)))
            { int slot = recoverySlot; Defer(() => StoryRecovery.Save(slot)); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            recoverySlot = Mathf.Clamp(recoverySlot, 1, 9);
            if (GUILayout.Button("减少", style.SubtleStyle, GUILayout.Width(56f * scale)))
            {
                recoverySlot = Mathf.Max(1, recoverySlot - 1);
                confirmRestoreMain = confirmRestoreBackup = false;
            }
            GUILayout.Label("槽位 " + recoverySlot, style.Entry, GUILayout.Width(64f * scale));
            if (GUILayout.Button("增加", style.SubtleStyle, GUILayout.Width(56f * scale)))
            {
                recoverySlot = Mathf.Min(9, recoverySlot + 1);
                confirmRestoreMain = confirmRestoreBackup = false;
            }
            GUILayout.EndHorizontal();
            string blocker = StoryReplay.Blocker(recoverySlot);
            string backupBlocker = StoryReplay.Blocker(recoverySlot, true);
            bool canRestore = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = canRestore && blocker.Length == 0;
            if (GUILayout.Button(confirmRestoreMain ? "确认恢复主检查点？" : "恢复检查点",
                style.ButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH)))
            {
                bool armed = confirmRestoreMain;
                confirmRestoreMain = false; confirmRestoreBackup = false;
                if (armed) { int slot = recoverySlot; Defer(() => StoryReplay.Start(slot)); }
                else confirmRestoreMain = true;
            }
            GUI.enabled = canRestore && backupBlocker.Length == 0;
            if (GUILayout.Button(confirmRestoreBackup ? "确认恢复历史备份？" : "恢复历史备份",
                style.SubtleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH)))
            {
                bool armed = confirmRestoreBackup;
                confirmRestoreMain = false; confirmRestoreBackup = false;
                if (armed) { int slot = recoverySlot; Defer(() => StoryReplay.Start(slot, true)); }
                else confirmRestoreBackup = true;
            }
            GUI.enabled = canRestore;
            GUILayout.EndHorizontal();
            if (blocker.Length > 0) Note("现在不能恢复：" + blocker);
            if (!string.IsNullOrEmpty(StoryRecovery.Status)) Note(StoryRecovery.Status);
            if (!string.IsNullOrEmpty(StoryReplay.Status)) Note(StoryReplay.Status);
            if (StoryReplay.Active) Note("重放中：第 " + StoryReplay.Position + " 个事件");
            Note("恢复需要二次确认，将覆盖当前未存档进度；历史备份是上一份检查点，不是原生存档。");
        }

        private void Recall()
        {
            if (DuelTools.Dialogue.Count == 0) { Note("本场还没有记录。回想最多保留 500 条，不暂停战斗。"); return; }
            foreach (string line in DuelTools.Dialogue) GUILayout.Label(line, style.Entry);
            Note("回想最多保留 500 条；新单挑开始时清空。");
        }

        private void BattlePage()
        {
            int view = ViewTabs(battleView, BattleViews);
            if (view != battleView) { battleView = view; scroll[1] = Vector2.zero; }
            GUILayout.Space(8f * scale);
            bool duel = battleView == 0;
            Note(duel ? "以下 7 项只影响单挑，群战另有一页。" : "以下 5 项只影响群战，单挑另有一页。");
            // Small right-aligned actions: the view switcher above already owns the
            // full-width bar look, so these must not compete with it.
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("全部开启", style.SubtleStyle, GUILayout.Width(84f * scale), GUILayout.Height(24f * scale))) SetBattle(duel, true);
            if (GUILayout.Button("全部关闭", style.SubtleStyle, GUILayout.Width(84f * scale), GUILayout.Height(24f * scale))) SetBattle(duel, false);
            GUILayout.EndHorizontal();
            if (duel) DuelSection(); else GroupSection();
        }

        private void SetBattle(bool duel, bool on)
        {
            if (duel)
            {
                plugin.Options.DuelHealth.Value = on;
                plugin.Options.DuelStamina.Value = on;
                plugin.Options.DuelDodge.Value = on;
                plugin.Options.DuelAlwaysHit.Value = on;
                plugin.Options.DuelFirstStrike.Value = on;
                plugin.Options.DuelZeroDamage.Value = on;
                plugin.Options.DuelDamage.Value = on;
            }
            else
            {
                plugin.Options.GroupHealth.Value = on;
                plugin.Options.GroupMana.Value = on;
                plugin.Options.GroupCooldown.Value = on;
                plugin.Options.GroupFreeze.Value = on;
                plugin.Options.GroupDown.Value = on;
            }
            plugin.Options.Config.Save();
            plugin.Status = (duel ? "单挑" : "群战") + (on ? "辅助已全部开启。" : "辅助已全部关闭。");
        }

        private void DuelSection()
        {
            plugin.Options.DuelHealth.Value = DrawSwitchRow("主角生命不减少", plugin.Options.DuelHealth.Value,
                "回合扣血与持续伤害归零；剧情强制改值与胜负保留。");
            plugin.Options.DuelStamina.Value = DrawSwitchRow("无限气力", plugin.Options.DuelStamina.Value,
                "保持单挑行动气力；不改周目内力属性。");
            plugin.Options.DuelDodge.Value = DrawSwitchRow("最高闪避率", plugin.Options.DuelDodge.Value,
                "原生闪避拉满；必中特技与剧情结果仍生效。");
            plugin.Options.DuelAlwaysHit.Value = DrawSwitchRow("必定命中", plugin.Options.DuelAlwaysHit.Value,
                "攻击/兵器/绝招一律命中，不看命中率。");
            plugin.Options.DuelFirstStrike.Value = DrawSwitchRow("必定先手", plugin.Options.DuelFirstStrike.Value,
                "行动对比判你赢，敌方本回合行动失效。");
            plugin.Options.DuelZeroDamage.Value = DrawSwitchRow("造成伤害降为0", plugin.Options.DuelZeroDamage.Value,
                "敌方所受伤害归零（含持续伤害），与一击必杀互斥。");
            plugin.Options.DuelDamage.Value = DrawSwitchRow("一击必杀", plugin.Options.DuelDamage.Value,
                "有效伤害直接清空敌方生命；自带必中与先手。");
        }

        private void GroupSection()
        {
            plugin.Options.GroupHealth.Value = DrawSwitchRow("主角生命不减少", plugin.Options.GroupHealth.Value,
                "仍会硬直；友军与剧情退场按原生规则。");
            plugin.Options.GroupMana.Value = DrawSwitchRow("气力保持充足", plugin.Options.GroupMana.Value,
                "只影响群战气力条。");
            plugin.Options.GroupCooldown.Value = DrawSwitchRow("技能栏立即冷却", plugin.Options.GroupCooldown.Value,
                "保留原生施放限制与演出。");
            plugin.Options.GroupFreeze.Value = DrawSwitchRow("冻结剩余时间", plugin.Options.GroupFreeze.Value,
                "倒计时拉长；按计时归零获胜的关卡请关闭。");
            plugin.Options.GroupDown.Value = DrawSwitchRow("敌方受伤即倒地", plugin.Options.GroupDown.Value,
                "含友军与持续伤害；敌闪避、剧情无敌保留。");
        }

        private void GrowthPage()
        {
            int view = ViewTabs(growthView, GrowthViews);
            if (view != growthView) { growthView = view; scroll[2] = Vector2.zero; }
            GUILayout.Space(8f * scale);
            if (growthView == 1) { Relationships(); return; }
            Note("以下选项只在自由活动场景空闲时生效；练功与锻造炼丹两组按操作前后恢复点数与金钱。");
            Section("练功 · 3 项");
            plugin.Options.PracticeLimit.Value = DrawSwitchRow("解除修炼点数消耗上限", plugin.Options.PracticeLimit.Value,
                "练功不再限制单次消耗，按现有武学点数扣减。");
            plugin.Options.SkillFree.Value = DrawSwitchRow("练功消耗不减", plugin.Options.SkillFree.Value,
                "结束后把武学点数恢复原值；修炼条件仍须满足。");
            plugin.Options.FastSkill.Value = DrawSwitchRow("快速练功", plugin.Options.FastSkill.Value,
                "一次完成一级；升级奖励与条件照旧。");
            Section("锻造与炼丹 · 2 项");
            plugin.Options.ForgeFree.Value = DrawSwitchRow("锻造炼丹消耗不减", plugin.Options.ForgeFree.Value,
                "结束后把点数与金钱恢复原值；不解锁研发条件。");
            plugin.Options.FastForge.Value = DrawSwitchRow("快速锻造炼丹", plugin.Options.FastForge.Value,
                "一次完成一级；保留等级上限与奖励。");
            Section("日常行动 · 1 项");
            plugin.Options.UnlimitedActions.Value = DrawSwitchRow("无限日常行动点", plugin.Options.UnlimitedActions.Value,
                "行动点至少为 1 且不减；日期不会因点数用完前进。");
        }

        private void Relationships()
        {
            // 心上人 first: it is a single headline setting, and hiding it below the
            // character list made it look like the feature was missing.
            Section("心上人");
            if (SyncLoverFromGame())
            {
                loverIndex = GUILayout.SelectionGrid(loverIndex, Lovers, 5, style.Segment);
                GUILayout.Space(6f * scale);
                if (GUILayout.Button("应用心上人", style.ButtonStyle, GUILayout.Height(RowH)))
                    plugin.Status = ProgressionTools.SetLover(loverIndex);
                Note("可选九位候选或「自己」；不补齐结缘事件，正常存档后保留。");
            }
            else Note("当前场景不能修改：等加载完成、关闭游戏内面板，且不在战斗中。");

            Section("好感上限");
            plugin.Options.MaxAffection.Value = DrawSwitchRow("已登场角色保持各自好感上限", plugin.Options.MaxAffection.Value,
                "不解锁未登场角色；关闭后不会还原已写入的值。");

            Section("角色好感");
            if (!GameAccess.CanEditValues && !plugin.Options.PreviewEditable.Value)
            {
                Note("当前场景不能修改：等加载完成、关闭游戏内面板，且不在战斗中。");
                return;
            }
            var list = PlayerStatManagerData.Instance.Relationships.List;
            int shown = 0;
            foreach (var item in list)
            {
                if (item == null || !item.Active) continue;
                shown++;
                int maximum = GameAccess.Field<int>(item, "_max");
                string key = item.Type.ToString();
                string buffer;
                if (!relationshipEdits.TryGetValue(key, out buffer)) { buffer = item.Value.ToString(); relationshipEdits[key] = buffer; }
                GUILayout.BeginHorizontal();
                GUILayout.Label(key, style.Entry, GUILayout.ExpandWidth(true));
                GUILayout.Label(item.Value + " / " + maximum, style.Tight, GUILayout.Width(Col(.26f)));
                buffer = GUILayout.TextField(buffer, 5, GUILayout.Width(Col(.18f)), GUILayout.Height(RowH));
                relationshipEdits[key] = buffer;
                if (GUILayout.Button("应用", style.ButtonStyle, GUILayout.Width(Col(.16f)), GUILayout.Height(RowH)))
                {
                    int parsed;
                    if (!item.Active) plugin.Status = "该角色已下场，请刷新后重试。";
                    else if (!int.TryParse(buffer, out parsed) || parsed < 0 || parsed > maximum)
                        plugin.Status = key + " 的好感必须在 0～" + maximum + " 之间。";
                    else if (!GameAccess.CanEditValues) plugin.Status = "预览模式：只读，不写入。";
                    else
                    {
                        plugin.Gate.NotifyMutation("修改角色好感");
                        item.SetValue(parsed);
                        relationshipEdits[key] = item.Value.ToString();
                        plugin.Status = key + " 好感已改为 " + item.Value + "；正常存档后保留。";
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (shown == 0) Note("当前周目还没有已登场角色。");
            else Note("只列已登场角色；不会补齐恋爱事件或解锁未登场角色。");
        }

        // Keep the grid on the value the game actually holds instead of a stale pick.
        private bool SyncLoverFromGame()
        {
            if (!GameAccess.CanEditValues && !plugin.Options.PreviewEditable.Value) return false;
            var stat = PlayerStatManagerData.Instance == null ? null : PlayerStatManagerData.Instance.Stats.Get(GameStatType.愛人);
            if (stat == null) return false;
            int index = Array.IndexOf(ProgressionTools.LoverIds, stat.Value);
            if (index >= 0) loverIndex = index;
            return true;
        }

        private void NumericPage()
        {
            Section("属性");
            StatsBody();
            Section("物品数量");
            ItemsBody();
            Section("游戏速度");
            GUILayout.BeginHorizontal();
            float speed = GUILayout.HorizontalSlider(plugin.Options.Speed.Value, 0.25f, 5f, GUILayout.ExpandWidth(true));
            GUILayout.Label(speed.ToString("0.00") + "×", style.FooterValue, GUILayout.Width(64f * scale));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.5×", style.SubtleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH))) plugin.ApplySpeed(0.5f);
            if (GUILayout.Button("1×", style.SubtleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH))) plugin.ApplySpeed(1f);
            if (GUILayout.Button("1.5×", style.SubtleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH))) plugin.ApplySpeed(1.5f);
            if (GUILayout.Button("2×", style.SubtleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH))) plugin.ApplySpeed(2f);
            if (GUILayout.Button("3×", style.SubtleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH))) plugin.ApplySpeed(3f);
            GUILayout.EndHorizontal();
            if (Mathf.Abs(speed - plugin.Options.Speed.Value) > 0.02f) plugin.ApplySpeed(speed);
            Note("只改引擎时间倍率；原生暂停、慢动作与快进各有自己的状态。");
            UndoSection();
        }

        private void StatsBody()
        {
            var player = PlayerStatManagerData.Instance;
            if (player == null) { Note("尚未进入游戏进度；读档后这里会列出可改的属性。"); return; }
            bool editable = GameAccess.CanEditValues || (plugin != null && plugin.Options.PreviewEditable.Value);
            if (!editable) Note("当前不能修改（战斗中 / 加载中 / 恢复中）；下面是只读预览。");
            GUILayout.BeginHorizontal();
            GUILayout.Label("筛选", style.Description, GUILayout.Width(45f * scale));
            searchEdit = GUILayout.TextField(searchEdit, 80, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            foreach (var stat in player.Stats.List)
            {
                if (stat == null || !Editable.Contains(stat.StatType.ToString())) continue;
                string name = DisplayName(stat);
                if (!string.IsNullOrEmpty(search) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string key = stat.StatType.ToString();
                string buffer;
                if (!statEdits.TryGetValue(key, out buffer)) { buffer = stat.Value.ToString(); statEdits[key] = buffer; }
                GUILayout.BeginHorizontal();
                Color statColor = GUI.color;
                if (Flashing("stat:" + key)) GUI.color = new Color(1f, .90f, .66f);
                GUILayout.Label(Fit(name, style.Entry, Col(.40f) - 34f * scale) +
                    "  <color=#B7AE98>" + stat.Value + "</color>", style.Entry, GUILayout.Width(Col(.40f)));
                GUI.color = statColor;
                GUILayout.Label(stat.Min + "～" + stat.Max, style.Tight, GUILayout.Width(Col(.24f)));
                if (!editable)
                {
                    GUILayout.EndHorizontal();
                    continue;
                }
                buffer = GUILayout.TextField(buffer, 9, GUILayout.Width(Col(.21f)), GUILayout.Height(RowH));
                statEdits[key] = buffer;
                if (GUILayout.Button("应用", style.ButtonStyle, GUILayout.Width(Col(.14f)), GUILayout.Height(RowH)))
                {
                    int number;
                    if (!player.Stats.List.Contains(stat)) plugin.Status = "属性列表已变化，请重新打开本页。";
                    else if (!int.TryParse(buffer, out number) || number < stat.Min || number > stat.Max)
                        plugin.Status = name + " 必须在 " + stat.Min + "～" + stat.Max + " 之间。";
                    else
                    {
                        int old = stat.Value;
                        plugin.Gate.NotifyMutation("修改属性 " + key);
                        stat.SetValue(number);
                        statEdits[key] = stat.Value.ToString();
                        Record("属性 " + name + " " + old + "→" + stat.Value, () => stat.SetValue(old));
                        plugin.Status = name + " 已改为 " + stat.Value + "；正常存档后保留。";
                        Flash("stat:" + key);
                    }
                }
                GUILayout.EndHorizontal();
            }
            Note("属性名后面是当前值，中间列是允许范围；只读预览时输入框会灰掉。");
        }

        private void ItemsBody()
        {
            bool editable = GameAccess.CanEditValues || (plugin != null && plugin.Options.PreviewEditable.Value);
            if (!editable) Note("当前不能修改（战斗中 / 加载中 / 恢复中）；下面是只读预览。");
            var database = ItemDatabase.Instance;
            if (database == null) { Note("物品数据库尚未就绪。"); return; }
            int tab = ViewTabs((int)itemType - 1, ItemViews);
            itemType = (GameItemType)(tab + 1);
            showAllItems = DrawSwitchRow("也列出数量为 0 的物品", showAllItems, "勾选后可以给未持有的物品直接设置数量，等同于生成。");
            GUILayout.BeginHorizontal();
            GUILayout.Label("筛选", style.Description, GUILayout.Width(45f * scale));
            itemSearchEdit = GUILayout.TextField(itemSearchEdit, 80, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            Note("可按名称或物品 ID 搜索；每行右侧直接输入数量后点「应用」。");
            ItemDataCollection collection = tab == 0 ? database.Books : tab == 1 ? database.Miscs
                : tab == 2 ? database.Special : database.Consumables;
            if (collection == null || collection.List == null) { Note("该分类物品列表尚未就绪。"); return; }
            int shown = 0;
            foreach (var item in collection.List)
            {
                if (item == null || (!showAllItems && item.CurrentCount <= 0)) continue;
                string name = ItemName(item);
                if (!string.IsNullOrEmpty(itemSearch)
                    && name.IndexOf(itemSearch, StringComparison.OrdinalIgnoreCase) < 0
                    && item.Id.IndexOf(itemSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;
                string key = itemType + ":" + item.Id;
                string buffer;
                if (!itemEdits.TryGetValue(key, out buffer)) { buffer = item.CurrentCount.ToString(); itemEdits[key] = buffer; }
                GUILayout.BeginHorizontal();
                Color itemColor = GUI.color;
                if (Flashing("item:" + key)) GUI.color = new Color(1f, .90f, .66f);
                GUILayout.Label(Fit(name, style.Entry, Col(.44f)), style.Entry, GUILayout.Width(Col(.44f)));
                GUI.color = itemColor;
                GUILayout.Label("×" + item.CurrentCount, style.Tight, GUILayout.Width(Col(.16f)));
                buffer = GUILayout.TextField(buffer, 7, GUILayout.Width(Col(.20f)), GUILayout.Height(RowH));
                itemEdits[key] = buffer;
                if (GUILayout.Button("应用", style.ButtonStyle, GUILayout.Width(Col(.14f)), GUILayout.Height(RowH)))
                {
                    int number;
                    if (!int.TryParse(buffer, out number) || number < 0 || number > 999999)
                        plugin.Status = name + " 的数量必须在 0～999999 之间。";
                    else
                    {
                        int previous = item.CurrentCount;
                        plugin.Gate.NotifyMutation("修改物品 " + item.Id);
                        item.SetCount(number);
                        itemEdits[key] = item.CurrentCount.ToString();
                        Record("物品 " + name + " " + previous + "→" + item.CurrentCount, () => item.SetCount(previous));
                        plugin.Status = name + " 数量已改为 " + item.CurrentCount + "；正常存档后保留。";
                        Flash("item:" + key);
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (shown == 0) Note("没有匹配的物品；清空筛选或勾选「列出数量为 0 的物品」。");
        }



        private void SavePage()
        {
            // 存档格数 is the primary setting here; notes and sorting stay secondary.
            Section("存档格数");
            int configured = plugin.Options.SlotCount.Value;
            bool custom = configured >= 2 && configured <= 999;
            GUILayout.Label(custom ? "当前生效：" + configured + " 格" : "当前生效：游戏默认", style.Entry);
            GUILayout.BeginHorizontal();
            if (string.IsNullOrEmpty(slotCountText)) slotCountText = configured.ToString();
            slotCountText = GUILayout.TextField(slotCountText, 4, GUILayout.Width(72f * scale), GUILayout.Height(RowH));
            if (GUILayout.Button("保存格数", style.ButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH)))
            {
                int parsed;
                if (!int.TryParse(slotCountText, out parsed) || parsed < 2 || parsed > 999)
                    plugin.Status = "格数必须是 2～999 的整数；0 表示使用游戏原值。";
                else
                {
                    plugin.Options.SlotCount.Value = parsed;
                    plugin.Options.Config.Save();
                    plugin.Status = "格数已保存为 " + parsed + "；重新打开存档或读档界面后生效。";
                }
            }
            GUILayout.EndHorizontal();
            {
                // Two-click confirm on one widget: the control count never changes, so
                // the layout pass and the repaint pass always agree.
                bool armed = confirmResetSlots;
                if (GUILayout.Button(armed ? "确认恢复默认？" : ("恢复默认" + (custom ? "" : "（当前已是默认）")), style.DangerStyle,
                        GUILayout.Width(168f * scale), GUILayout.Height(RowH)))
                {
                    confirmResetSlots = false;
                    if (armed) Defer(() =>
                    {
                        plugin.Options.SlotCount.Value = 0; slotCountText = "0";
                        plugin.Options.Config.Save();
                        plugin.Status = "已恢复游戏默认格数。";
                    });
                    else confirmResetSlots = true;
                }
            }
            Note("只影响存档/读档列表里建多少个槽位，不改动已有存档文件。");

            SubSection("存档备注");
            string selected = SaveSlotComments.Selected;
            if (string.IsNullOrEmpty(selected))
                Note("当前选中槽位：无。在游戏存档列表点一个槽位即可选中。");
            else
            {
                GUILayout.Label("当前选中槽位：" + selected, style.Entry);
                if (commentSlot != selected) { commentSlot = selected; slotComment = SaveSlotComments.Get(selected); }
                slotComment = GUILayout.TextField(slotComment, 60, GUILayout.Height(RowH));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("保存备注", style.ButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(RowH)))
                { SaveSlotComments.Set(selected, slotComment); plugin.Status = "备注已保存。"; }
                if (GUILayout.Button("清空", style.SubtleStyle, GUILayout.Width(64f * scale), GUILayout.Height(RowH)))
                { Defer(() => { SaveSlotComments.Set(selected, ""); slotComment = ""; plugin.Status = "备注已清空。"; }); }
                GUILayout.EndHorizontal();
            }
            var comments = SaveSlotComments.All();
            if (comments.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("已有备注 " + comments.Count + " 条", style.Description, GUILayout.ExpandWidth(true));
                bool armed = confirmClearComments;
                if (GUILayout.Button(armed ? "确认清空？" : "全部清空", style.DangerStyle,
                        GUILayout.Width(96f * scale), GUILayout.Height(RowH)))
                {
                    confirmClearComments = false;
                    if (armed) Defer(() => { SaveSlotComments.Clear(); plugin.Status = "全部备注已清空。"; });
                    else confirmClearComments = true;
                }
                GUILayout.EndHorizontal();
                foreach (var pair in comments)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(pair.Key, style.Entry, GUILayout.Width(44f * scale));
                    GUILayout.Label(pair.Value, style.Description, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("删除", style.DangerStyle, GUILayout.Width(52f * scale), GUILayout.Height(RowH)))
                    { string slot = pair.Key; Defer(() => { SaveSlotComments.Set(slot, ""); plugin.Status = "已删除槽位 " + slot + " 的备注。"; }); }
                    GUILayout.EndHorizontal();
                }
            }
            else Note("还没有备注；按 F2 或在上方编辑框保存后会出现在这里。");

            SubSection("排序");
            plugin.Options.SortRecent.Value = DrawSwitchRow("按最近存档排序（F3 切换）", plugin.Options.SortRecent.Value,
                "只重排读档列表顺序；槽位编号与存档文件不变。");
        }

        private void DrawBottomBar()
        {
            float s = scale;
            GUILayout.Space(12f * s);
            Rule();
            GUILayout.BeginHorizontal(GUILayout.Height(44f * s));
            GUILayout.Label("界面透明度", style.FooterLabel, GUILayout.Width(76f * s));
            float inner = shownRect.width - (style.Skin != null ? style.Skin.window.padding.horizontal : 32f);
            float sliderWidth = Mathf.Clamp(inner - (76f + 48f + 72f) * s - 30f * s, 40f * s, 300f * s);
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
            // The hanging-scroll design caps both ends. The layout already reserved the
            // space below (the footer subtracts inset + rollH), only the draw call was
            // missing, which left the collapsed badge looking cut off at the bottom.
            GUI.DrawTexture(new Rect(rollX, badge.yMax - inset - rollH, rollW, rollH), style.ScrollRoll,
                ScaleMode.StretchToFill, true);
            float footerH = Mathf.Min(22f * s, badge.height * .17f);
            float footerY = badge.yMax - inset - rollH - footerH - 5f * s;
            float titleY = badge.y + inset + rollH + 8f * s;
            float titleH = Mathf.Max(1f, footerY - titleY - 6f * s);
            float sideInset = Mathf.Min(7f * s, badge.width * .18f);
            GUI.DrawTexture(new Rect(badge.x + sideInset, titleY, line, titleH), style.AccentSoft);
            if (badgeTitleStyle == null)
                badgeTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                    clipping = TextClipping.Overflow
                };
            badgeTitleStyle.font = style.BadgeFont ?? style.UiFont;
            badgeTitleStyle.fontSize = Mathf.Clamp(Mathf.FloorToInt(titleH / 3.3f), 11, 24);
            badgeTitleStyle.normal.textColor = new Color(.92f, .88f, .79f, 1f);
            GUI.Label(new Rect(badge.x + 2f * s, titleY, badge.width - 4f * s, titleH), "萬\n象\n錄", badgeTitleStyle);
            float tagWidth = Mathf.Max(1f, badge.width - 14f * s);
            Rect tagRect = new Rect(badge.x + (badge.width - tagWidth) * .5f, footerY, tagWidth, footerH);
            GUI.DrawTexture(new Rect(badge.x + 10f * s, footerY - 3f * s, Mathf.Max(1f, badge.width - 20f * s), line), style.AccentSoft);
            if (badgeKeyStyle == null)
                badgeKeyStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                    clipping = TextClipping.Overflow
                };
            badgeKeyStyle.font = style.UiFont;
            badgeKeyStyle.fontSize = Mathf.Clamp(Mathf.FloorToInt(footerH * .62f), 11, 20);
            badgeKeyStyle.normal.textColor = new Color(.90f, .76f, .48f, 1f);
            GUI.Label(new Rect(tagRect.x, tagRect.y - 1f, tagRect.width, tagRect.height + 2f),
                plugin.Options.Toggle.Value.ToString(), badgeKeyStyle);

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

        // Clicks on the panel itself must not fall through to the game UI. A
        // full-screen shield also swallowed every click outside the panel (duels
        // became unplayable), so it now covers exactly the open panel.

        private void EnsureBlocker(Rect shown)
        {
            if (blocker == null)
            {
                blocker = new GameObject("MortalHelper.PointerShield", typeof(Canvas), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(blocker);
                var canvas = blocker.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
                var child = new GameObject("Block", typeof(RectTransform), typeof(Image));
                child.transform.SetParent(blocker.transform, false);
                blockerRect = (RectTransform)child.transform;
                blockerRect.anchorMin = Vector2.zero; blockerRect.anchorMax = Vector2.zero;
                blockerRect.pivot = Vector2.zero;
                child.GetComponent<Image>().color = Color.clear;
            }
            if (!blocker.activeSelf) blocker.SetActive(true);
            blockerRect.anchoredPosition = new Vector2(shown.x, Screen.height - shown.yMax);
            blockerRect.sizeDelta = new Vector2(Mathf.Max(0f, shown.width), Mathf.Max(0f, shown.height));
        }

        private void HideBlocker()
        {
            if (blocker != null && blocker.activeSelf) blocker.SetActive(false);
        }

        // A clickable section header so long blocks can be folded away.
        // Lighter heading for secondary blocks so the page keeps a clear hierarchy.
        private void SubSection(string title)
        {
            GUILayout.Space(16f * scale);
            GUILayout.Label("· " + title, style.Description);
            Rule();
            GUILayout.Space(4f * scale);
        }


        private bool Fold(string title, bool open, Action toggle)
        {
            GUILayout.Space(12f * scale);
            if (GUILayout.Button((open ? "▾ " : "▸ ") + title, style.Entry, GUILayout.ExpandWidth(true)))
                Defer(toggle);
            Rule();
            if (open) GUILayout.Space(6f * scale);
            return open;
        }


        private void Section(string title)
        {
            GUILayout.Space(24f * scale);
            // The accent bar and the title used to be laid out independently, so the
            // bar sat on the row top while the glyph block sat lower. Reserve one row
            // from the measured text height and centre both inside it.
            float width = Mathf.Max(60f, ContentWidth());
            float textHeight = style.SectionTitle.CalcHeight(new GUIContent(title), width);
            float rowHeight = Mathf.Max(RowH, Mathf.Ceil(textHeight));
            Rect row = GUILayoutUtility.GetRect(1f, rowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
            float barWidth = Mathf.Max(2f, Mathf.Round(2f * scale));
            // Measured at scale 1: the glyph ink is 16px tall and sits 3px below the
            // line-box centre, so a bar matched to the line box reads as too high and
            // too long next to the title.
            float barHeight = Mathf.Clamp(textHeight - 4f * scale, 10f * scale, 18f * scale);
            float barY = row.y + Mathf.Round((rowHeight - barHeight) * .5f) + Mathf.Round(3f * scale);
            GUI.DrawTexture(new Rect(row.x, barY, barWidth, barHeight), style.Accent);
            float textX = row.x + barWidth + Mathf.Round(8f * scale);
            GUI.Label(new Rect(textX, row.y, Mathf.Max(10f, row.xMax - textX), rowHeight), title, style.SectionTitle);
            GUILayout.Space(2f * scale);
            Rule();
            GUILayout.Space(12f * scale);
        }

        // View switcher drawn as underlined text tabs. GUI.Toolbar fills the whole
        // width with solid bars, which read like the action buttons right below it.
        private int ViewTabs(int current, string[] labels)
        {
            int chosen = current;
            GUILayout.BeginHorizontal();
            float height = 30f * scale;
            for (int i = 0; i < labels.Length; i++)
            {
                bool active = i == current;
                Rect rect = GUILayoutUtility.GetRect(1f, height, GUILayout.Width(96f * scale), GUILayout.Height(height));
                bool hover = rect.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.MouseDown && hover) { chosen = i; Event.current.Use(); }
                if (Event.current.type == EventType.Repaint)
                {
                    Color before = GUI.color;
                    GUI.color = active ? new Color(.95f, .91f, .82f)
                        : hover ? new Color(.86f, .81f, .70f) : new Color(.68f, .64f, .56f);
                    GUI.Label(rect, labels[i], style.Tab);
                    GUI.color = before;
                    if (active)
                        GUI.DrawTexture(new Rect(rect.x + 10f * scale, rect.yMax - 2f * scale,
                            Mathf.Max(1f, rect.width - 20f * scale), Mathf.Max(2f, 2f * scale)), style.Accent);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f * scale);
            return chosen;
        }

        private void Rule()
        {
            Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
            GUI.DrawTexture(r, style.Hairline);
        }
        private bool DrawSwitchRow(string label, bool value, string hint = null)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(Mathf.Round(24f * scale)));
            float controls = 52f * scale + 22f * scale + 16f * scale;
            GUILayout.Label(label, style.Entry, GUILayout.Width(Mathf.Max(120f, ContentWidth() - controls)));
            bool next = GUILayout.Toggle(value, GUIContent.none, style.SwitchTrack,
                GUILayout.Width(52f * scale), GUILayout.Height(Mathf.Round(24f * scale)));
            GUILayout.Label(next ? "开" : "关", next ? style.SwitchOnLabel : style.SwitchOffLabel,
                GUILayout.Width(22f * scale));
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(hint))
            {
                GUILayout.Space(1f * scale);
                Wrapped(hint, style.Description);
            }
            GUILayout.Space(6f * scale);
            return next;
        }


        // IMGUI sizes a wrapped label from the width it saw during Layout, which is
        // unreliable inside a scroll view; a 2-line note then overflows onto the next
        // widget. Measure and reserve the height explicitly.
        // Every value edit keeps an undo closure so a mistyped number is one click
        // away from being put back.
        private void Record(string label, Action undo)
        {
            editLog.Insert(0, new EditRecord { Label = label, Undo = undo });
            if (editLog.Count > 8) editLog.RemoveAt(editLog.Count - 1);
        }

        private void UndoSection()
        {
            if (editLog.Count == 0) return;
            editOpen = Fold("修改记录 · " + editLog.Count + " 条", editOpen, () => editOpen = !editOpen);
            if (!editOpen) return;
            for (int i = 0; i < editLog.Count; i++)
            {
                var record = editLog[i];
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("撤销", style.SubtleStyle, GUILayout.Width(Col(.16f))))
                {
                    var item = record;
                    if (!GameAccess.CanEditValues) plugin.Status = "当前场景不能撤销修改。";
                    else Defer(() =>
                    {
                        try { item.Undo(); editLog.Remove(item); plugin.Status = "已撤销：" + item.Label; }
                        catch (Exception error) { plugin.Status = "撤销失败：" + error.Message; }
                    });
                }
                GUILayout.EndHorizontal();
            }
        }


        // Keep the status line honest: success reads warm, a refusal reads red.
        private static bool StatusLooksBad(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string bad in new[] { "失败", "不能", "必须", "错误", "超出", "不允许", "尚未", "无法", "不可" })
                if (text.IndexOf(bad, StringComparison.Ordinal) >= 0) return true;
            return false;
        }


        private void Note(string text)
        {
            Wrapped(text, style.Description);
        }

        private void Wrapped(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Explicit width: IMGUI measures and draws with the same value, so a wrapped
            // line can never exceed the space that was reserved for it.
            GUILayout.Label(text, style, GUILayout.Width(ContentWidth()));
        }

        // A control must be tall enough for its glyph plus style padding, otherwise the
        private float RowH => Mathf.Max(24f, Mathf.Round(27f * scale));

        private float Col(float fraction) { return ContentWidth() * fraction; }

        // Brief row highlight after a successful write, so the eye lands on the row
        // that changed instead of only noticing the status line.
        // Cached style copies: allocating them per repaint churned the GC. They are
        // rebuilt only when the panel scale changes (font sizes live in PanelStyle).
        private GUIStyle tabActiveStyle, badgeTitleStyle, badgeKeyStyle;
        private float tabActiveScale = -1f;

        private string flashKey;
        private float flashUntil;
        private void Flash(string key) { flashKey = key; flashUntil = Time.unscaledTime + .3f; }
        private bool Flashing(string key) { return key != null && key == flashKey && Time.unscaledTime < flashUntil; }

        private float ContentWidth()
        {
            var pad = style.Skin != null ? style.Skin.window.padding.horizontal : 32f;
            float bar = style.Skin != null ? style.Skin.verticalScrollbar.fixedWidth : 7f;
            // A very wide window used to stretch switches away from their labels and
            // spread the page tabs across the whole title bar. Cap the layout width and
            // let the extra space stay as margin instead.
            float cap = Mathf.Round(720f * Mathf.Max(1f, scale));
            return Mathf.Max(60f, Mathf.Min(shownRect.width - pad - bar - 10f, cap));
        }

        // Single-line names are clipped by IMGUI with no marker; truncate with an
        // ellipsis so a long item or stat name does not look like it was cut by bug.
        // The result is cached per text/width/font size and found with a binary search:
        // this runs for every row on every repaint, so a scan restarted each frame was
        // measurable (26 stat rows plus the item list).
        private readonly Dictionary<string, string> fitCache = new Dictionary<string, string>();

        private string Fit(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || style == null || width <= 12f) return text;
            string cacheKey = text + "|" + (int)width + "|" + style.fontSize;
            string cached;
            if (fitCache.TryGetValue(cacheKey, out cached)) return cached;
            float limit = width - 2f;
            string result = text;
            if (style.CalcSize(new GUIContent(text)).x > limit)
            {
                int low = 0, high = text.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    string candidate = text.Substring(0, mid) + "…";
                    if (style.CalcSize(new GUIContent(candidate)).x <= limit) low = mid;
                    else high = mid - 1;
                }
                result = low <= 0 ? "…" : text.Substring(0, low) + "…";
            }
            if (fitCache.Count > 512) fitCache.Clear();
            fitCache[cacheKey] = result;
            return result;
        }

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
        internal void ClearSelection() { statEdits.Clear(); itemEdits.Clear(); }
        internal void Dispose()
        {
            if (blocker != null) UnityEngine.Object.Destroy(blocker);
            style.Dispose();
        }
    }
}
