using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MortalHelper.Game
{
    // Both maps use the game's textures. World names and nodes follow the coordinates
    // recorded from its travel-map scene, while each view keeps its own zoom and pan.
    internal sealed class MapViewer : IDisposable
    {
        private const string WorldKey = "Assets/__Project/Images/Background/screen_map.png";
        private const string SectKey = "Assets/__Project/Images/Background/screen_section_001_map_02.png";
        private static readonly string[] PlaceNames = {
            "唐門", "點蒼", "青城", "峨嵋", "全真", "崆峒", "嵩山", "六道峽", "江陵",
            "江陵府", "襄陽", "南陽", "錦香宮", "南嵩山", "福建", "雪山", "火燄山劍閣"
        };
        private static readonly Vector2[] PlacePoints = {
            new Vector2(625.5f, 475.5f), new Vector2(444.7f, 729.1f), new Vector2(474f, 358f),
            new Vector2(418.2f, 509.7f), new Vector2(832f, 270.1f), new Vector2(656.2f, 181f),
            new Vector2(977.7f, 252.2f), new Vector2(967.1f, 346.4f), new Vector2(1026f, 440.7f),
            new Vector2(1431.1f, 397.9f), new Vector2(1036.7f, 376.2f), new Vector2(1085.1f, 347.6f),
            new Vector2(1053.4f, 500.1f), new Vector2(1356.1f, 551.4f), new Vector2(1419.3f, 597.5f),
            new Vector2(501.6f, 239.8f), new Vector2(536.2f, 170.4f)
        };
        private static readonly Vector2[] NodePoints = {
            new Vector2(644.6f, 234.6f), new Vector2(528.7f, 299.3f), new Vector2(503.7f, 456.4f),
            new Vector2(449.7f, 601.7f), new Vector2(461.2f, 778.7f), new Vector2(834.2f, 348.4f),
            new Vector2(969.2f, 321.4f), new Vector2(1010f, 542.7f), new Vector2(1023.5f, 487.3f),
            new Vector2(1074.6f, 450.1f), new Vector2(994.9f, 451.8f), new Vector2(755.9f, 641f)
        };

        private readonly AsyncOperationHandle<Texture2D>[] handles = new AsyncOperationHandle<Texture2D>[2];
        private readonly bool[] requested = { false, false };
        private readonly float[] zoom = { 1f, 1f };
        private readonly Vector2[] pan = { Vector2.zero, Vector2.zero };
        private Texture2D redDot;
        private Font songti;
        private GUIStyle placeStyle;
        private int dragControl;
        private int view;

        internal int View => view;

        internal void SetView(int next)
        {
            if (next < 0 || next > 1 || next == view) return;
            EndDrag();
            view = next;
        }

        internal void ResetView()
        {
            zoom[view] = 1f;
            pan[view] = Vector2.zero;
            EndDrag();
        }

        internal void EndDrag()
        {
            if (dragControl != 0 && GUIUtility.hotControl == dragControl) GUIUtility.hotControl = 0;
            dragControl = 0;
        }

        internal void Draw(Rect viewport, PanelStyle style, Func<string, string> localize)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Texture2D texture = Acquire();
            if (Event.current.type == EventType.Layout) return;
            GUI.DrawTexture(viewport, style.Dark);
            if (texture == null)
            {
                bool failed = requested[view] && handles[view].IsValid() && handles[view].IsDone;
                GUI.Label(viewport, localize(failed ? "地图暂时无法载入。" : "正在载入地图…"), style.Description);
                if (failed && GUI.Button(new Rect(viewport.x + 12f, viewport.y + 32f, 80f, 28f),
                    localize("重试"), style.ButtonStyle)) Dispose();
                return;
            }

            float fit = Mathf.Min(viewport.width / texture.width, viewport.height / texture.height);
            Event input = Event.current;
            if (input.type == EventType.ScrollWheel && viewport.Contains(input.mousePosition))
            {
                float next = Mathf.Clamp(zoom[view] * Mathf.Pow(1.15f, -input.delta.y), 1f, 6f);
                Vector2 pointer = input.mousePosition - viewport.center;
                pan[view] = pointer - (pointer - pan[view]) * (next / zoom[view]);
                zoom[view] = next;
                input.Use();
            }
            if (input.type == EventType.MouseDown && input.button == 0 && viewport.Contains(input.mousePosition))
            {
                dragControl = control;
                GUIUtility.hotControl = control;
                input.Use();
            }
            if (input.type == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                pan[view] += input.delta;
                input.Use();
            }
            if (input.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                EndDrag();
                input.Use();
            }
            Vector2 size = new Vector2(texture.width * fit * zoom[view], texture.height * fit * zoom[view]);
            float maxX = Mathf.Max(0f, (size.x - viewport.width) * .5f);
            float maxY = Mathf.Max(0f, (size.y - viewport.height) * .5f);
            pan[view].x = Mathf.Clamp(pan[view].x, -maxX, maxX);
            pan[view].y = Mathf.Clamp(pan[view].y, -maxY, maxY);
            GUI.BeginGroup(viewport);
            GUI.DrawTexture(new Rect((viewport.width - size.x) * .5f + pan[view].x,
                (viewport.height - size.y) * .5f + pan[view].y, size.x, size.y), texture, ScaleMode.StretchToFill);
            if (view == 0) DrawPlaces(viewport, size, style);
            GUI.EndGroup();
        }

        private Texture2D Acquire()
        {
            if (!requested[view])
            {
                requested[view] = true;
                handles[view] = Addressables.LoadAssetAsync<Texture2D>(view == 0 ? WorldKey : SectKey);
            }
            if (!handles[view].IsDone) return null;
            if (handles[view].Status == AsyncOperationStatus.Succeeded) return handles[view].Result;
            return null;
        }

        private void DrawPlaces(Rect viewport, Vector2 size, PanelStyle style)
        {
            float factor = size.x / 1920f;
            Vector2 origin = new Vector2((viewport.width - size.x) * .5f + pan[view].x,
                (viewport.height - size.y) * .5f + pan[view].y);
            if (redDot == null) CreateRedDot();
            float dotSize = Mathf.Max(4f, 14f * factor);
            foreach (Vector2 point in NodePoints)
            {
                Vector2 center = origin + point * factor;
                GUI.DrawTexture(new Rect(center.x - dotSize * .5f, center.y - dotSize * .5f,
                    dotSize, dotSize), redDot);
            }
            if (placeStyle == null)
            {
                try { songti = Font.CreateDynamicFontFromOSFont(new[] { "SimSun", "宋体" }, 28); }
                catch (Exception) { }
                placeStyle = new GUIStyle(GUI.skin.label) {
                    alignment = TextAnchor.UpperCenter, wordWrap = false,
                    normal = { textColor = new Color(.70f, .10f, .08f, 1f) }
                };
            }
            placeStyle.font = songti != null ? songti : style.UiFont;
            placeStyle.fontSize = Mathf.Max(7, Mathf.RoundToInt(28f * factor));
            float step = Mathf.Max(8f, 30f * factor);
            for (int i = 0; i < PlaceNames.Length; i++)
            {
                Vector2 point = origin + PlacePoints[i] * factor;
                for (int j = 0; j < PlaceNames[i].Length; j++)
                    GUI.Label(new Rect(point.x, point.y + j * step, step, step + 2f),
                        PlaceNames[i][j].ToString(), placeStyle);
            }
        }

        private void CreateRedDot()
        {
            redDot = new Texture2D(14, 14, TextureFormat.RGBA32, false);
            for (int y = 0; y < 14; y++)
                for (int x = 0; x < 14; x++)
                {
                    float radius = new Vector2(x - 6.5f, y - 6.5f).magnitude;
                    redDot.SetPixel(x, y, radius > 6.5f ? Color.clear :
                        radius > 5f ? new Color(.39f, .04f, .03f, 1f) : new Color(.82f, .13f, .10f, 1f));
                }
            redDot.Apply(false, true);
        }

        public void Dispose()
        {
            EndDrag();
            for (int i = 0; i < handles.Length; i++)
            {
                if (requested[i] && handles[i].IsValid()) Addressables.Release(handles[i]);
                requested[i] = false;
            }
            if (redDot != null) UnityEngine.Object.Destroy(redDot);
            if (songti != null) UnityEngine.Object.Destroy(songti);
            redDot = null;
            songti = null;
        }
    }
}
