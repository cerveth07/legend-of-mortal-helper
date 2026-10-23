using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MortalHelper.Game
{
    internal sealed class MapViewer : IDisposable
    {
        private const string MapKey = "Assets/__Project/Images/Background/screen_section_001_map_02.png";
        private AsyncOperationHandle<Texture2D> handle;
        private bool requested;
        private float zoom = 1f;
        private Vector2 pan;
        private int dragControl;

        internal void ResetView() { zoom = 1f; pan = Vector2.zero; EndDrag(); }

        internal void EndDrag()
        {
            if (dragControl != 0 && GUIUtility.hotControl == dragControl) GUIUtility.hotControl = 0;
            dragControl = 0;
        }

        internal void Draw(Rect viewport, PanelStyle style, Func<string, string> localize)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (!requested)
            {
                requested = true;
                handle = Addressables.LoadAssetAsync<Texture2D>(MapKey);
            }
            if (Event.current.type == EventType.Layout) return;
            GUI.DrawTexture(viewport, style.Dark);
            if (!handle.IsDone)
            {
                GUI.Label(viewport, localize("正在载入地图…"), style.Description);
                return;
            }
            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                GUI.Label(viewport, localize("地图暂时无法载入。"), style.Description);
                if (GUI.Button(new Rect(viewport.x + 12f, viewport.y + 32f, 80f, 28f), localize("重试"), style.ButtonStyle))
                    Dispose();
                return;
            }

            Texture2D texture = handle.Result;
            float fit = Mathf.Min(viewport.width / texture.width, viewport.height / texture.height);
            Event input = Event.current;
            if (input.type == EventType.ScrollWheel && viewport.Contains(input.mousePosition))
            {
                float next = Mathf.Clamp(zoom * Mathf.Pow(1.15f, -input.delta.y), 1f, 6f);
                Vector2 pointer = input.mousePosition - viewport.center;
                pan = pointer - (pointer - pan) * (next / zoom);
                zoom = next;
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
                pan += input.delta;
                input.Use();
            }
            if (input.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                EndDrag();
                input.Use();
            }
            Vector2 size = new Vector2(texture.width * fit * zoom, texture.height * fit * zoom);
            float maxX = Mathf.Max(0f, (size.x - viewport.width) * .5f);
            float maxY = Mathf.Max(0f, (size.y - viewport.height) * .5f);
            pan.x = Mathf.Clamp(pan.x, -maxX, maxX);
            pan.y = Mathf.Clamp(pan.y, -maxY, maxY);
            GUI.BeginGroup(viewport);
            GUI.DrawTexture(new Rect((viewport.width - size.x) * .5f + pan.x,
                (viewport.height - size.y) * .5f + pan.y, size.x, size.y), texture, ScaleMode.StretchToFill);
            GUI.EndGroup();
        }

        public void Dispose()
        {
            EndDrag();
            if (requested && handle.IsValid()) Addressables.Release(handle);
            requested = false;
        }
    }
}
