using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MortalHelper.Core
{
    // Version strings are provenance, not a schema. Preserve every recorded field;
    // additions inherit the current native loader's baseline defaults. No field or
    // stable ID may silently disappear. Unrecorded new gameplay state must not drift.
    public static class CheckpointCompatibility
    {
        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string> {
            { "Stats", "Key" }, { "Relationships", "Key" }, { "EventFlags", "Name" },
            { "Talents", "Id" }, { "Books", "Id" }, { "Miscs", "Id" }, { "SpecialItems", "Id" }
        };
        public static void ValidateSchema(string json, Type schema) { Validate(Parse(json), schema, "$", 0); }
        private static void Validate(object node, Type type, string path, int depth)
        {
            if (depth > 64) throw new InvalidDataException("存档结构过深：" + path);
            if (node == null)
            {
                if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
                    throw new InvalidDataException("非空字段不能恢复为空：" + path);
                return;
            }
            var map = node as Dictionary<string, object>;
            if (map != null)
            {
                if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(IEnumerable).IsAssignableFrom(type))
                    throw new InvalidDataException("存档对象类型已改变：" + path);
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(f => f.Name, StringComparer.Ordinal);
                foreach (var pair in map)
                {
                    FieldInfo field;
                    if (!fields.TryGetValue(pair.Key, out field)) throw new InvalidDataException("新版本缺少旧存档字段，需要专用迁移：" + path + "." + pair.Key);
                    Validate(pair.Value, field.FieldType, path + "." + pair.Key, depth + 1);
                }
                return;
            }
            var list = node as List<object>;
            if (list != null)
            {
                Type item = type.IsArray ? type.GetElementType() : type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type)
                    ? type.GetGenericArguments()[0] : null;
                if (item == null) throw new InvalidDataException("存档列表类型已改变：" + path);
                foreach (var value in list) Validate(value, item, path + "[]", depth + 1);
                return;
            }
            bool valid = node is string ? type == typeof(string) : node is bool ? type == typeof(bool) :
                node is Number && (type.IsEnum || type.IsPrimitive && type != typeof(bool) && type != typeof(char));
            if (!valid) throw new InvalidDataException("存档字段类型已改变：" + path);
            if (node is Number)
            {
                try { Convert.ChangeType(((Number)node).Text, type.IsEnum ? Enum.GetUnderlyingType(type) : type, CultureInfo.InvariantCulture); }
                catch (Exception e) { throw new InvalidDataException("旧字段数值不适合当前类型：" + path, e); }
            }
        }
        public static bool HasField(string json, string field) => Object(Parse(json)).ContainsKey(field);
        public static bool Equivalent(string recorded, string baselineDefaults, string actual, bool legacyPresentation = false)
        {
            var old = Object(Parse(recorded)); var defaults = Object(Parse(baselineDefaults)); var live = Object(Parse(actual));
            // These fields are metadata, or a newly introduced presentation-only log.
            old.Remove("TimeTick"); defaults.Remove("TimeTick"); live.Remove("TimeTick");
            old.Remove("Version"); defaults.Remove("Version"); live.Remove("Version");
            if (legacyPresentation || !old.ContainsKey("StoryLogs")) { old.Remove("StoryLogs"); defaults.Remove("StoryLogs"); live.Remove("StoryLogs"); }
            return Equal(Merge(old, defaults, ""), live, "");
        }
        private static object Merge(object recorded, object defaults, string path)
        {
            var old = recorded as Dictionary<string, object>; var current = defaults as Dictionary<string, object>;
            if (old != null && current != null)
            {
                var result = new Dictionary<string, object>(current, StringComparer.Ordinal);
                foreach (var pair in old)
                {
                    object value;
                    if (!current.TryGetValue(pair.Key, out value)) throw new InvalidDataException("旧字段不能丢弃：" + path + "." + pair.Key);
                    result[pair.Key] = Merge(pair.Value, value, path.Length == 0 ? pair.Key : path + "." + pair.Key);
                }
                return result;
            }
            var a = recorded as List<object>; var b = defaults as List<object>; string key;
            if (a != null && b != null && Keys.TryGetValue(path, out key))
            {
                var expected = Index(a, key, path); var native = Index(b, key, path);
                foreach (var pair in expected)
                    if (!native.ContainsKey(pair.Key)) throw new InvalidDataException("当前版本缺少旧数据编号：" + path + "/" + pair.Key);
                return b.Select(x => { string id = Identity(x, key); object previous;
                    return expected.TryGetValue(id, out previous) ? Merge(previous, x, path + "[]") : x; }).ToList();
            }
            return recorded;
        }
        private static bool Equal(object a, object b, string path)
        {
            if (a == null || b == null) return a == b;
            var x = a as Dictionary<string, object>; var y = b as Dictionary<string, object>;
            if (x != null && y != null) return x.Count == y.Count && x.All(p => y.ContainsKey(p.Key) && Equal(p.Value, y[p.Key], path.Length == 0 ? p.Key : path + "." + p.Key));
            var u = a as List<object>; var v = b as List<object>; string key;
            if (u != null && v != null)
            {
                if (Keys.TryGetValue(path, out key))
                {
                    var left = Index(u, key, path); var right = Index(v, key, path);
                    return left.Count == right.Count && left.All(p => right.ContainsKey(p.Key) && Equal(p.Value, right[p.Key], path + "[]"));
                }
                if (u.Count != v.Count) return false;
                return u.Select((item, i) => Equal(item, v[i], path + "[]")).All(ok => ok);
            }
            var n = a as Number; var m = b as Number;
            // Retain exact number spelling: no floating-point rounding tolerance.
            return n != null && m != null ? n.Text == m.Text : a.Equals(b);
        }
        private static Dictionary<string, object> Index(List<object> values, string key, string path)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (object value in values)
            {
                string id = Identity(value, key);
                object previous;
                if (result.TryGetValue(id, out previous))
                {
                    // Native flag collections can share the same asset. Only identical
                    // flag aliases are collapsible; conflicting values remain ambiguous.
                    if (path == "EventFlags" && Equal(previous, value, path + "[]")) continue;
                    throw new InvalidDataException("稳定编号重复或状态冲突，不能猜测迁移：" + key + "=" + id);
                }
                result.Add(id, value);
            }
            return result;
        }
        private static string Identity(object node, string key)
        {
            object value; var map = Object(node);
            if (!map.TryGetValue(key, out value) || value == null || value is Dictionary<string, object> || value is List<object>)
                throw new InvalidDataException("存档稳定编号缺失：" + key);
            return value is Number ? ((Number)value).Text : value.ToString();
        }
        private static Dictionary<string, object> Object(object value) => value as Dictionary<string, object> ?? throw new InvalidDataException("存档 JSON 对象无效。");
        private sealed class Number { internal string Text; }
        private static object Parse(string json) => new Reader(json).Read();
        private sealed class Reader
        {
            private readonly string source; private int at, budget;
            internal Reader(string source) { if (source == null || source.Length > 12000000) throw new InvalidDataException("存档 JSON 大小无效。"); this.source = source; }
            internal object Read() { object value = Value(0); Space(); if (at != source.Length) Bad(); return value; }
            private void Space() { while (at < source.Length && char.IsWhiteSpace(source[at])) at++; }
            private void Bad() { throw new InvalidDataException("存档 JSON 格式无效，位置 " + at); }
            private bool Take(char c) { Space(); if (at < source.Length && source[at] == c) { at++; return true; } return false; }
            private object Value(int depth)
            {
                if (depth > 64 || ++budget > 500000) throw new InvalidDataException("存档 JSON 复杂度超限。");
                Space(); if (at == source.Length) Bad();
                char c = source[at];
                if (c == '"') return String();
                if (Take('{'))
                {
                    var map = new Dictionary<string, object>(StringComparer.Ordinal); if (Take('}')) return map;
                    do { Space(); if (at == source.Length || source[at] != '"') Bad(); string key = String();
                        if (map.ContainsKey(key) || !Take(':')) Bad(); map.Add(key, Value(depth + 1)); } while (Take(','));
                    if (!Take('}')) Bad(); return map;
                }
                if (Take('['))
                {
                    var list = new List<object>(); if (Take(']')) return list;
                    do { list.Add(Value(depth + 1)); } while (Take(',')); if (!Take(']')) Bad(); return list;
                }
                foreach (string literal in new[] { "true", "false", "null" })
                    if (at + literal.Length <= source.Length && string.Compare(source, at, literal, 0, literal.Length, StringComparison.Ordinal) == 0) { at += literal.Length; return literal == "null" ? null : (object)(literal == "true"); }
                int start = at; if (source[at] == '-') at++;
                if (at == source.Length || !(source[at] >= '0' && source[at] <= '9')) Bad();
                if (source[at] == '0') at++; else while (at < source.Length && source[at] >= '0' && source[at] <= '9') at++;
                if (at < source.Length && source[at] == '.') { at++; int digits = at; while (at < source.Length && (source[at] >= '0' && source[at] <= '9')) at++; if (at == digits) Bad(); }
                if (at < source.Length && (source[at] == 'e' || source[at] == 'E')) { at++; if (at < source.Length && (source[at] == '+' || source[at] == '-')) at++;
                    int digits = at; while (at < source.Length && (source[at] >= '0' && source[at] <= '9')) at++; if (at == digits) Bad(); }
                return new Number { Text = source.Substring(start, at - start) };
            }
            private string String()
            {
                at++; var text = new StringBuilder();
                while (at < source.Length)
                {
                    char c = source[at++]; if (c == '"') return text.ToString(); if (c < 32) Bad();
                    if (c != '\\') { text.Append(c); continue; }
                    if (at == source.Length) Bad(); c = source[at++];
                    const string escapes = "\"\\/bfnrt"; int index = escapes.IndexOf(c);
                    if (index >= 0) { text.Append("\"\\/\b\f\n\r\t"[index]); continue; }
                    if (c != 'u' || at + 4 > source.Length) Bad(); ushort code;
                    if (!ushort.TryParse(source.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) Bad();
                    text.Append((char)code); at += 4;
                }
                Bad(); return null;
            }
        }
    }
}
