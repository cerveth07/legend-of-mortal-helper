using UnityEngine;

namespace MortalHelper.Game
{
    // Parchment/scroll visual system reproduced from the previous panel design:
    // same palette, borders, switch and roll textures, and the KaiTi badge title.
    // Only presentation lives here; no game or feature logic.
    internal sealed class PanelStyle
    {
        internal float Scale = 1f;
        internal Font UiFont, BadgeFont;
        internal GUISkin Skin;

        internal Texture2D Dark, Panel, Surface, SurfaceAlt, Accent, AccentSoft, Hover, Transparent,
            Button, ButtonHover, ButtonActive, Input, SwitchOn, SwitchOff, SliderTrack, SliderFill,
            SliderThumb, ScrollPaper, ScrollRoll, Border, Ink, Badge, BadgeHover, BadgeActive,
            Hairline, Focus, SegOff;

        internal GUIStyle Title, SectionTitle, Tab, TabHover, TabHit, Segment, Description, Tight,
            Entry, SwitchTrack, SwitchState, SwitchOnLabel, SwitchOffLabel, DangerStyle, FooterLabel, FooterValue, ButtonStyle, SubtleStyle, IconButton, Notice;

        private static readonly Color Primary = new Color(.92f, .88f, .79f, 1f);
        private static readonly Color Secondary = new Color(.76f, .72f, .63f, 1f);
        private static readonly Color AccentColor = new Color(.83f, .64f, .35f, 1f);
        private static readonly Color DangerColor = new Color(.94f, .55f, .45f, 1f);

        internal void Build()
        {
            Dark = Solid(new Color(.045f, .04f, .035f, 1f));
            Panel = Solid(new Color(.075f, .068f, .06f, .78f));
            Surface = Solid(new Color(.16f, .14f, .115f, .22f));
            SurfaceAlt = Solid(new Color(.20f, .16f, .115f, .30f));
            Accent = Solid(new Color(.78f, .55f, .27f, 1f));
            AccentSoft = Solid(new Color(.78f, .55f, .27f, .34f));
            Hover = Solid(new Color(.78f, .55f, .27f, .14f));
            Button = Bordered(new Color(.11f, .095f, .075f, .72f), new Color(.55f, .42f, .26f, .62f));
            ButtonHover = Bordered(new Color(.19f, .145f, .09f, .82f), new Color(.78f, .55f, .27f, .86f));
            ButtonActive = Bordered(new Color(.32f, .22f, .12f, .92f), new Color(.88f, .67f, .36f, .95f));
            Input = Bordered(new Color(.10f, .085f, .07f, .85f), new Color(.62f, .50f, .32f, .85f));
            Hairline = Solid(new Color(.78f, .62f, .36f, .22f));
            SegOff = Bordered(new Color(.16f, .14f, .11f, .55f), new Color(.55f, .42f, .26f, .45f));
            Focus = Bordered(new Color(0f, 0f, 0f, 0f), new Color(.83f, .64f, .35f, .95f));
            Transparent = Solid(new Color(0f, 0f, 0f, 0f));
            SwitchOff = MakeSwitch(72, 34, false);
            SwitchOn = MakeSwitch(72, 34, true);
            SliderTrack = Solid(new Color(.30f, .26f, .20f, 1f));
            SliderFill = Solid(new Color(.78f, .55f, .27f, .96f));
            SliderThumb = MakeCircle(24, 24, new Color(.94f, .82f, .59f, 1f), new Color(.52f, .39f, .22f, .95f));
            ScrollPaper = Solid(new Color(.78f, .69f, .50f, .97f));
            ScrollRoll = MakeRoll(18, 34);
            Border = Solid(new Color(.55f, .42f, .25f, .78f));
            Ink = MakeInk(240, 180);
            Badge = Solid(new Color(.045f, .038f, .03f, .80f));
            BadgeHover = Solid(new Color(.10f, .075f, .042f, .88f));
            BadgeActive = Solid(new Color(.15f, .105f, .055f, .94f));
            BadgeFont = CreateBadgeFont();
            Title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            SectionTitle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft };
            Tab = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            TabHover = new GUIStyle(Tab);
            TabHit = new GUIStyle(GUIStyle.none);
            Segment = new GUIStyle(GUI.skin.button);
            Description = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            Tight = new GUIStyle(GUI.skin.label) { wordWrap = false, richText = false, clipping = TextClipping.Clip };
            Entry = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            SwitchTrack = new GUIStyle(GUI.skin.toggle);
            SwitchState = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
            SwitchOnLabel = new GUIStyle(SwitchState);
            SwitchOffLabel = new GUIStyle(SwitchState);
            FooterLabel = new GUIStyle(GUI.skin.label);
            FooterValue = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
            ButtonStyle = new GUIStyle(GUI.skin.button);
            SubtleStyle = new GUIStyle(GUI.skin.button);
            IconButton = new GUIStyle(GUI.skin.button);
            DangerStyle = new GUIStyle(GUI.skin.button);
            Notice = new GUIStyle(GUI.skin.box);
        }

        internal void Refresh(float scale, Font uiFont)
        {
            Scale = scale;
            if (uiFont != null) UiFont = uiFont;
            int size = Mathf.Max(14, (int)(16 * Scale));
            foreach (var style in new[] { Tab, TabHover, Description, Entry,
                SwitchState, SwitchOnLabel, SwitchOffLabel, FooterLabel, FooterValue, ButtonStyle, IconButton, Notice })
                style.font = UiFont;
            var headingFont = BadgeFont != null ? BadgeFont : UiFont;

            Title.fontSize = size + 4; Title.normal.textColor = Primary;
            Title.alignment = TextAnchor.MiddleCenter; Title.wordWrap = false; Title.clipping = TextClipping.Clip;
            Title.padding = new RectOffset(0, 0, 0, 0); Title.margin = new RectOffset(0, 0, 0, 0); Title.contentOffset = Vector2.zero;
            SectionTitle.fontSize = size + 1; SectionTitle.normal.textColor = AccentColor;
            Tab.fontSize = Mathf.Max(12, size - 2); Tab.normal.textColor = Secondary;
            TabHover.fontSize = Mathf.Max(12, size - 2); TabHover.normal.textColor = Primary;
            Description.fontSize = Mathf.Max(12, size - 2); Description.normal.textColor = Secondary;
            Tight.font = UiFont; Tight.fontSize = Mathf.Max(12, size - 2); Tight.normal.textColor = Secondary;
            Entry.fontSize = size; Entry.normal.textColor = Primary;
            SwitchState.fontSize = Mathf.Max(11, size - 3); SwitchState.normal.textColor = Secondary;
            SwitchOnLabel.fontSize = SwitchState.fontSize; SwitchOnLabel.normal.textColor = AccentColor;
            SwitchOffLabel.fontSize = SwitchState.fontSize; SwitchOffLabel.normal.textColor = Secondary;
            DangerStyle.fontSize = Mathf.Max(11, size - 3); DangerStyle.normal.textColor = DangerColor;
            DangerStyle.font = UiFont; DangerStyle.padding = new RectOffset(7, 7, 3, 3);
            DangerStyle.margin = new RectOffset(0, 0, 0, 0); DangerStyle.border = new RectOffset(1, 1, 1, 1);
            DangerStyle.normal.background = Button; DangerStyle.hover.background = ButtonHover;
            DangerStyle.active.background = ButtonActive; DangerStyle.focused.background = ButtonHover;
            DangerStyle.hover.textColor = new Color(1f, .66f, .58f);
            DangerStyle.active.textColor = new Color(1f, .72f, .66f);
            FooterLabel.fontSize = Mathf.Max(11, size - 3); FooterLabel.normal.textColor = Secondary;
            FooterValue.fontSize = Mathf.Max(11, size - 3); FooterValue.normal.textColor = AccentColor;

            if (Skin != null) Object.Destroy(Skin);
            Skin = Object.Instantiate(GUI.skin);
            Skin.font = UiFont;
            Skin.label.font = UiFont; Skin.label.fontSize = size; Skin.label.normal.textColor = Primary; Skin.label.wordWrap = true;

            var button = Skin.button;
            button.font = UiFont; button.fontSize = Mathf.Max(12, size - 2); button.alignment = TextAnchor.MiddleCenter;
            button.padding = new RectOffset(8, 8, 3, 3);
            button.normal.background = Transparent; button.hover.background = Hover;
            button.active.background = AccentSoft; button.focused.background = Hover;
            button.onNormal.background = AccentSoft; button.onHover.background = Accent;
            button.onActive.background = Accent; button.onFocused.background = AccentSoft;
            button.normal.textColor = Secondary; button.hover.textColor = Primary;
            button.active.textColor = Primary; button.focused.textColor = Primary;
            button.onNormal.textColor = Primary; button.onHover.textColor = Primary;
            button.onActive.textColor = Primary; button.onFocused.textColor = Primary;

            var toggle = Skin.toggle;
            toggle.font = UiFont; toggle.fontSize = size; toggle.normal.textColor = Primary;
            toggle.onNormal.textColor = Primary; toggle.hover.textColor = Primary; toggle.onHover.textColor = Primary;
            toggle.normal.background = Transparent; toggle.onNormal.background = Transparent;

            var field = Skin.textField;
            field.font = UiFont; field.fontSize = size; field.normal.textColor = Primary; field.focused.textColor = Primary;
            field.normal.background = Input; field.focused.background = Focus; field.hover.background = Input;
            field.padding = new RectOffset(8, 8, 3, 3); field.border = new RectOffset(1, 1, 1, 1);

            var window = Skin.window;
            window.font = UiFont; window.fontSize = size; window.normal.textColor = Primary;
            window.normal.background = Transparent; window.onNormal.background = Transparent;
            window.active.background = Transparent; window.focused.background = Transparent;
            window.padding = new RectOffset(16, 16, 8, 14); window.border = new RectOffset(1, 1, 1, 1);

            var box = Skin.box;
            box.font = UiFont; box.fontSize = size; box.normal.background = Surface; box.normal.textColor = Secondary;
            box.padding = new RectOffset(8, 8, 7, 7);

            Skin.horizontalSlider.normal.background = SliderTrack; Skin.horizontalSlider.fixedHeight = 8f * Scale;
            Skin.horizontalSlider.margin = new RectOffset(0, 0, 8, 8);
            Skin.horizontalSliderThumb.normal.background = SliderThumb;
            Skin.horizontalSliderThumb.fixedWidth = 18f * Scale; Skin.horizontalSliderThumb.fixedHeight = 18f * Scale;
            Skin.verticalScrollbar.normal.background = Transparent;
            Skin.verticalScrollbarThumb.normal.background = AccentSoft;
            Skin.verticalScrollbarThumb.hover.background = Accent;
            Skin.verticalScrollbarThumb.fixedWidth = 7f * Scale;
            Skin.verticalScrollbar.fixedWidth = 8f * Scale;

            Segment.font = UiFont; Segment.fontSize = Mathf.Max(12, size - 2);
            Segment.padding = new RectOffset(8, 8, 3, 3); Segment.margin = new RectOffset(2, 2, 0, 0);
            Segment.normal.background = SegOff; Segment.hover.background = Hover;
            Segment.active.background = AccentSoft; Segment.focused.background = Hover;
            Segment.onNormal.background = AccentSoft; Segment.onHover.background = AccentSoft;
            Segment.onActive.background = Accent; Segment.onFocused.background = AccentSoft;
            Segment.normal.textColor = Secondary; Segment.hover.textColor = Primary;
            Segment.active.textColor = Primary; Segment.focused.textColor = Primary;
            Segment.onNormal.textColor = Primary; Segment.onHover.textColor = Primary;
            Segment.onActive.textColor = Primary; Segment.onFocused.textColor = Primary;

            ButtonStyle.font = UiFont; ButtonStyle.padding = new RectOffset(8, 8, 3, 3); ButtonStyle.fontSize = Mathf.Max(12, size - 2);
            ButtonStyle.normal.background = Button; ButtonStyle.hover.background = ButtonHover;
            ButtonStyle.active.background = ButtonActive; ButtonStyle.focused.background = ButtonHover;
            ButtonStyle.normal.textColor = Primary; ButtonStyle.hover.textColor = Primary;
            ButtonStyle.active.textColor = Primary; ButtonStyle.focused.textColor = Primary;
            SubtleStyle.font = UiFont; SubtleStyle.fontSize = Mathf.Max(12, size - 2);
            SubtleStyle.padding = new RectOffset(7, 7, 3, 3);
            SubtleStyle.normal.background = Transparent; SubtleStyle.hover.background = Hover; SubtleStyle.active.background = AccentSoft;
            SubtleStyle.normal.textColor = Secondary; SubtleStyle.hover.textColor = Primary; SubtleStyle.active.textColor = Primary;
            IconButton.font = UiFont; IconButton.fontSize = size + 2; IconButton.alignment = TextAnchor.MiddleCenter;
            IconButton.normal.background = Transparent; IconButton.hover.background = Hover; IconButton.active.background = AccentSoft;
            IconButton.normal.textColor = Secondary; IconButton.hover.textColor = Primary; IconButton.active.textColor = AccentColor;
            Notice.font = UiFont; Notice.padding = new RectOffset(10, 10, 6, 6);
            Notice.normal.background = SurfaceAlt; Notice.normal.textColor = AccentColor;
            Notice.fontSize = Mathf.Max(12, size - 2);

            SwitchTrack.font = UiFont; SwitchTrack.fixedWidth = 52f * Scale; SwitchTrack.fixedHeight = 25f * Scale;
            SwitchTrack.normal.background = SwitchOff; SwitchTrack.hover.background = SwitchOff; SwitchTrack.active.background = SwitchOff;
            SwitchTrack.onNormal.background = SwitchOn; SwitchTrack.onHover.background = SwitchOn; SwitchTrack.onActive.background = SwitchOn;
            SwitchTrack.border = new RectOffset(0, 0, 0, 0); SwitchTrack.padding = new RectOffset(0, 0, 0, 0);
        }

        internal void SetOpacity(float value)
        {
            if (Panel == null) return;
            value = Mathf.Clamp(value, .4f, 1f);
            Panel.SetPixel(0, 0, new Color(.075f, .068f, .06f, value));
            Panel.Apply(false, false);
        }

        private static Font CreateBadgeFont()
        {
            try
            {
                var font = Font.CreateDynamicFontFromOSFont(new[] { "KaiTi", "楷体" }, 32);
                if (font != null) return font;
            }
            catch (System.Exception) { }
            return null;
        }

        private Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color); texture.Apply();
            return texture;
        }

        private static Texture2D Bordered(Color fill, Color border)
        {
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[9];
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                pixels[y * 3 + x] = (x == 0 || x == 2 || y == 0 || y == 2) ? border : fill;
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        private static Texture2D MakeCircle(int width, int height, Color fill, Color edge)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            float cx = (width - 1) * .5f, cy = (height - 1) * .5f, r = Mathf.Min(width, height) * .5f;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    pixels[y * width + x] = d <= r - 1 ? fill : d <= r ? edge : new Color(0, 0, 0, 0);
                }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        private static Texture2D MakeSwitch(int width, int height, bool on)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            Color background = on ? new Color(.72f, .48f, .22f, .98f) : new Color(.25f, .23f, .21f, .95f);
            Color edge = on ? new Color(.91f, .69f, .38f, 1f) : new Color(.47f, .43f, .38f, 1f);
            Color knob = new Color(.94f, .89f, .78f, 1f);
            float radius = height * .5f, knobRadius = height * .32f, knobX = on ? width - radius : radius;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x - width * .5f) - (width * .5f - radius), 0f);
                    float dy = Mathf.Abs(y - height * .5f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance <= radius)
                    {
                        float kx = x - knobX, ky = y - height * .5f, kdistance = Mathf.Sqrt(kx * kx + ky * ky);
                        pixels[y * width + x] = kdistance <= knobRadius ? knob
                            : Color.Lerp(background, edge, Mathf.Clamp01((kdistance - knobRadius) / Mathf.Max(1f, height * .18f)));
                    }
                    else pixels[y * width + x] = new Color(0, 0, 0, 0);
                }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        private static Texture2D MakeRoll(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            float cx = (width - 1) * .5f, cy = (height - 1) * .5f, rx = width * .5f, ry = height * .5f;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    float nx = (x - cx) / rx, ny = (y - cy) / ry;
                    if (nx * nx + ny * ny <= 1f)
                        pixels[y * width + x] = Color.Lerp(new Color(.33f, .24f, .14f, 1f), new Color(.80f, .63f, .36f, 1f),
                            1f - Mathf.Clamp01(Mathf.Abs(nx)));
                    else pixels[y * width + x] = new Color(0, 0, 0, 0);
                }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        private static Texture2D MakeInk(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    float dx = (x - width * .58f) / (width * .55f), dy = (y - height * .48f) / (height * .65f);
                    float wash = Mathf.Exp(-(dx * dx + dy * dy) * 2.2f) * .045f;
                    float bamboo = Mathf.Abs(x - (width * .22f + y * .18f + Mathf.Sin(y * .055f) * 6f));
                    if (bamboo < 2f) wash += .025f * (1f - bamboo / 2f);
                    pixels[y * width + x] = new Color(.68f, .62f, .53f, wash);
                }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        internal void Dispose()
        {
            foreach (var texture in new[] { Dark, Panel, Surface, SurfaceAlt, Accent, AccentSoft, Hover, Transparent,
                Button, ButtonHover, ButtonActive, Input, SwitchOn, SwitchOff, SliderTrack, SliderFill, SliderThumb,
                ScrollPaper, ScrollRoll, Border, Ink, Badge, BadgeHover, BadgeActive })
                if (texture != null) Object.Destroy(texture);
            if (Skin != null) Object.Destroy(Skin);
            Skin = null;
        }
    }
}
