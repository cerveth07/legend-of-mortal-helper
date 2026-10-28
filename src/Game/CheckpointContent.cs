using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Fungus;
using HarmonyLib;
using Mortal.Core;

namespace MortalHelper.Game
{
    internal static class CheckpointContent
    {
        private static readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Queue<string> Order = new Queue<string>();
        internal static string Key(string name)
        {
            name = (name ?? "").Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            return name.EndsWith(".LuaScript", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 10) : name;
        }
        internal static void Observe(string name, string source)
        {
            if (string.IsNullOrEmpty(source) || source.Length > 2000000) return;
            name = Key(name);
            if (!Sources.ContainsKey(name)) Order.Enqueue(name);
            Sources[name] = source;
            while (Order.Count > 16) Sources.Remove(Order.Dequeue());
        }
        internal static string SourceFor(string name) { string value; return Sources.TryGetValue(Key(name), out value) ? value : null; }
        internal static string HashFor(string name) { string value = SourceFor(name); return value == null ? null : Hash(value); }
        private static string Hash(string text)
        { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text))); }
        internal static void Validate(AnytimeStoryRecord record)
        {
            if (record.ScriptSource == null && record.ScriptHash == null) return;
            if (string.IsNullOrEmpty(record.ScriptSource) || record.ScriptSource.Length > 2000000 || record.ScriptHash != Hash(record.ScriptSource))
                throw new InvalidDataException("归档剧情脚本不完整或校验失败。");
        }
        internal static void Backup(string nativePath, string slot, string fromClient)
        {
            string root = Path.Combine(Path.GetDirectoryName(Plugin.Instance.Options.Config.ConfigFilePath), "checkpoint-backups");
            string identity = Hash(FileHash(nativePath) + "\n" + FileHash(nativePath + ".mha")).Replace('/', '_').Replace('+', '-').Substring(0, 20);
            string folder = Path.Combine(root, slot + "-" + identity);
            Directory.CreateDirectory(folder);
            foreach (string suffix in new[] { "", ".mha", ".mha-required" })
            {
                string source = nativePath + suffix;
                if (!File.Exists(source)) continue;
                string copy = Path.Combine(folder, Path.GetFileName(source));
                if (!File.Exists(copy)) File.Copy(source, copy);
                if (FileHash(source) != FileHash(copy)) throw new IOException("原档备份校验失败，兼容恢复未开始。" + copy);
            }
            File.WriteAllText(Path.Combine(folder, "info.txt"), "checkpoint-backup 1\nsource=" + fromClient + "\ntarget=" + UnityEngine.Application.version + "\n", new UTF8Encoding(false));
            Plugin.Instance?.Log("[Compatibility] original bundle backup=" + folder);
        }
        private static string FileHash(string path)
        { using (var file = File.OpenRead(path)) using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(file)); }
    }
    [HarmonyPatch(typeof(LuaEnvironment), "LoadLuaFunction")]
    internal static class CheckpointContentPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref string __0, string __1)
        {
            __0 = AnytimeStory.SourceForReplay(__0, __1);
            CheckpointContent.Observe(__1, __0);
        }
    }
}
