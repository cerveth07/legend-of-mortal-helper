using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    internal static class GameAccess
    {
        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
        public static T Field<T>(object target, string name)
        {
            if (target == null) throw new InvalidOperationException("游戏对象尚未就绪。");
            Type type = target.GetType();
            string key = type.AssemblyQualifiedName + "::" + name;
            FieldInfo field;
            if (!Fields.TryGetValue(key, out field))
            {
                field = AccessTools.Field(type, name);
                if (field == null || !typeof(T).IsAssignableFrom(field.FieldType))
                    throw new MissingFieldException(type.FullName, name);
                Fields.Add(key, field);
            }
            return (T)field.GetValue(target);
        }

        public static void Set<T>(object target, string name, T value)
        {
            Field<T>(target, name); // resolve and type-check first
            Fields[target.GetType().AssemblyQualifiedName + "::" + name].SetValue(target, value);
        }

        public static bool Typing
        {
            get
            {
                // IMGUI fields are not visible to Unity's EventSystem selection.
                if (Plugin.Instance?.Panel?.EditingTextField == true) return true;
                var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                return selected != null && (selected.GetComponent<InputField>() != null ||
                    selected.GetComponent<TMPro.TMP_InputField>() != null);
            }
        }

        public static bool Loaded => PlayerStatManagerData.Instance != null && SceneController.Instance != null &&
            !SceneController.Instance.IsLoading && !SceneController.Instance.IsPrepare &&
            SceneManager.GetActiveScene().name != "Title";

        public static bool CanEditNumbers => NumericEditBlockReason == null;
        public static string NumericEditBlockReason
        {
            get
            {
                string scene = SceneManager.GetActiveScene().name;
                if (PlayerStatManagerData.Instance == null || SceneController.Instance == null || scene == "Title")
                    return "请先开始游戏或读取存档。";
                if (SceneController.Instance.IsLoading || SceneController.Instance.IsPrepare)
                    return "场景正在加载，请稍候。";
                if (Plugin.Instance == null || !Plugin.Instance.Gate.CanInteract || AnytimeStory.Replaying || AnytimeDuel.Replaying)
                    return "存档恢复尚未完成，请等待恢复或重新读取有效存档。";
                if (scene == "Story" || scene == "Break") return AnytimeStory.NumericEditBlockReason;
                if (scene == "Combat") return AnytimeDuel.NumericEditBlockReason;
                return null;
            }
        }

        // Editing during story callbacks or free-roam processing can invalidate
        // in-flight conditions and animation state. Use the native idle boundary.
        public static bool CanEditValues => CanEdit;
        public static bool CanEdit => Loaded && Plugin.Instance != null && Plugin.Instance.Gate.CanInteract &&
            SceneManager.GetActiveScene().name == "Free" && Mortal.Free.FreeManager.Instance != null &&
            !Mortal.Free.FreeManager.Instance.IsProcessing && Mortal.Free.FreeManager.Instance.EnableHotKey;
    }
}
