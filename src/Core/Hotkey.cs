using System;
using System.Text;

namespace MortalHelper.Core
{
    // One shortcut as written in the config: "Tab", "Ctrl+Tab", "Alt+Shift+F9", "None".
    // Deliberately free of UnityEngine so the offline contract suite can cover the parser
    // without the game assemblies; whether a key name exists at all is decided by the
    // layer that owns KeyCode.
    internal struct Hotkey
    {
        internal const string Disabled = "None";

        internal bool Ctrl, Alt, Shift;
        internal string Key;

        internal bool Enabled
        {
            get
            {
                return !string.IsNullOrEmpty(Key) &&
                    !string.Equals(Key, Disabled, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static Hotkey From(bool ctrl, bool alt, bool shift, string key)
        {
            return new Hotkey { Ctrl = ctrl, Alt = alt, Shift = shift, Key = Clean(key) };
        }

        // Tolerant on input ("ctrl + tab", "TAB", "Tab+Ctrl"), canonical on output.
        internal static Hotkey Parse(string text)
        {
            bool ctrl = false, alt = false, shift = false;
            string key = null;
            if (!string.IsNullOrEmpty(text))
            {
                foreach (string part in text.Split('+'))
                {
                    string token = part.Trim();
                    if (token.Length == 0) continue;
                    if (Same(token, "ctrl") || Same(token, "control")) { ctrl = true; continue; }
                    if (Same(token, "alt")) { alt = true; continue; }
                    if (Same(token, "shift")) { shift = true; continue; }
                    // A second non-modifier token is a typo, not an alternative: keep the
                    // first so "Tab+Q" cannot silently become something else.
                    if (key == null) key = Clean(token);
                }
            }
            return new Hotkey { Ctrl = ctrl, Alt = alt, Shift = shift, Key = key ?? Disabled };
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string key)
        {
            if (string.IsNullOrEmpty(key)) return Disabled;
            var text = new StringBuilder(key.Length);
            foreach (char c in key.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_') text.Append(c);
                else break;
            }
            if (text.Length == 0 || text.Length > 24) return Disabled;
            return text.ToString();
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            if (Ctrl) text.Append("Ctrl+");
            if (Alt) text.Append("Alt+");
            if (Shift) text.Append("Shift+");
            text.Append(Enabled ? Key : Disabled);
            return text.ToString();
        }
    }
}
