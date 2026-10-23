using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Mortal.Core;
using UnityEngine;
#if !NO_PROBE
using HarmonyLib;
#endif

namespace MortalHelper.Game
{
    internal static class AnytimeStoryDiagnostics
    {
        internal static string Difference(string expected, string actual)
        {
            var differences = new List<string>();
            Compare(JsonUtility.FromJson<GameSave>(expected), JsonUtility.FromJson<GameSave>(actual), "$", differences, 0);
            return differences.Count == 0 ? "JSON 文本顺序或格式不同（字段值未发现差异）" : string.Join("; ", differences.ToArray());
        }
        private static void Compare(object expected, object actual, string path, List<string> differences, int depth)
        {
            if (differences.Count >= 12) return;
            if (depth > 64) { differences.Add(path + ": diagnostic depth limit"); return; }
            if (expected == null || actual == null)
            {
                if (!Equals(expected, actual)) differences.Add(path + ": expected=" + Value(expected) + ", actual=" + Value(actual));
                return;
            }
            var type = expected.GetType();
            if (type.IsPrimitive || type.IsEnum || expected is string || expected is decimal || type != actual.GetType())
            {
                if (!Equals(expected, actual)) differences.Add(path + ": expected=" + Value(expected) + ", actual=" + Value(actual));
                return;
            }
            var a = expected as IList; var b = actual as IList;
            if (a != null && b != null)
            {
                if (a.Count != b.Count) differences.Add(path + ".Count: expected=" + a.Count + ", actual=" + b.Count);
                for (int i = 0; i < Math.Min(a.Count, b.Count) && differences.Count < 12; i++)
                    Compare(a[i], b[i], path + "[" + i + "]", differences, depth + 1);
                return;
            }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (path == "$" && field.Name == "TimeTick") continue;
                Compare(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name, differences, depth + 1);
                if (differences.Count >= 12) return;
            }
        }
        private static string Value(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";
            text = text.Replace("\r", "\\r").Replace("\n", "\\n");
            return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
        }
#if !NO_PROBE
        internal static void TraceFlag(string phase, FlagData flag, int? value = null)
        {
            if (!AnytimeStory.Replaying || flag == null || flag.name != "S9200") return;
            Plugin.Instance?.Log("[Anytime] flag trace phase=" + phase + "; flag=" + flag.name +
                "; object=" + flag.GetInstanceID() + "; state=" + flag.State +
                (value.HasValue ? "; requested=" + value.Value : "") + "; stack=" + Environment.StackTrace);
        }
#endif
    }
#if !NO_PROBE
    [HarmonyPatch(typeof(MissionManagerData), "SetFlag")]
    internal static class AnytimeStoryFlagSetTrace
    {
        private static void Postfix(MissionManagerData __instance, string __0, int __1)
        {
            if (__0 == "S9200") AnytimeStoryDiagnostics.TraceFlag("SetFlag", __instance.GetFlag(__0), __1);
        }
    }
    [HarmonyPatch(typeof(FlagData), "ResetData")]
    internal static class AnytimeStoryFlagResetTrace
    {
        private static void Prefix(FlagData __instance) { AnytimeStoryDiagnostics.TraceFlag("ResetData-before", __instance); }
        private static void Postfix(FlagData __instance) { AnytimeStoryDiagnostics.TraceFlag("ResetData-after", __instance); }
    }
#endif
}
