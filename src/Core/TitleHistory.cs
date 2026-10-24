using System;
using System.Collections.Generic;
using System.Globalization;

namespace MortalHelper.Core
{
    // This state belongs to one loaded GameSave, never a slot or global preference.
    internal sealed class TitleHistory
    {
        internal const string Prefix = "mh.title.";
        private readonly SortedSet<int> acquired = new SortedSet<int>();
        internal IEnumerable<int> Acquired => acquired;
        internal int Selected { get; private set; } = -1;
        internal bool Contains(int id) => acquired.Contains(id);
        private static bool Valid(int id) => id >= 0 && id <= 999;

        internal void Reset(int current)
        {
            acquired.Clear(); Selected = -1; Observe(current);
        }
        internal void Observe(int id) { if (Valid(id)) acquired.Add(id); }
        internal bool Select(int id)
        {
            if (id != -1 && !Valid(id)) return false;
            Selected = id; return true;
        }
        internal void Load(IEnumerable<KeyValuePair<string, int>> entries, int current)
        {
            Reset(current);
            var data = new Dictionary<string, int>(StringComparer.Ordinal);
            if (entries != null)
                foreach (var item in entries)
                    if (item.Key != null && item.Key.StartsWith(Prefix, StringComparison.Ordinal)) data[item.Key] = item.Value;
            int version;
            if (!data.TryGetValue(Prefix + "version", out version) || version != 1) return;
            foreach (var item in data)
            {
                int id;
                if (item.Value == 1 && item.Key.StartsWith(Prefix + "owned.", StringComparison.Ordinal) &&
                    int.TryParse(item.Key.Substring((Prefix + "owned.").Length), NumberStyles.None, CultureInfo.InvariantCulture, out id)) Observe(id);
            }
            int selected;
            if (data.TryGetValue(Prefix + "selected", out selected)) Select(selected);
        }
        internal IEnumerable<KeyValuePair<string, int>> Save()
        {
            yield return new KeyValuePair<string, int>(Prefix + "version", 1);
            yield return new KeyValuePair<string, int>(Prefix + "selected", Selected);
            foreach (int id in acquired)
                yield return new KeyValuePair<string, int>(Prefix + "owned." + id.ToString(CultureInfo.InvariantCulture), 1);
        }
        internal static bool IsMetadata(string key) => key != null && key.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
