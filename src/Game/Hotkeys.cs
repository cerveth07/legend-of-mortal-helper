using System;
using BepInEx.Configuration;
using MortalHelper.Core;
using UnityEngine;

namespace MortalHelper.Game
{
    // Shortcuts the player can rebind. Panel is the only one needed to reach the panel
    // itself, which is why the panel UI owns the rebind control and the config file stays
    // the fallback when the panel cannot be opened at all.
    internal enum HotkeyAction { Panel, Comment, SortRecent }

    internal static class Hotkeys
    {
        // True while the rebind control owns the keyboard. Without it, pressing Tab to
        // assign Tab would close the panel before the key could be stored.
        internal static bool Capturing;

        internal static ConfigEntry<string> Entry(Settings options, HotkeyAction action)
        {
            if (action == HotkeyAction.Comment) return options.CommentKey;
            if (action == HotkeyAction.SortRecent) return options.SortKey;
            return options.PanelKey;
        }

        internal static Hotkey Read(Settings options, HotkeyAction action)
        {
            return Hotkey.Parse(Entry(options, action).Value);
        }

        internal static string Display(Settings options, HotkeyAction action)
        {
            Hotkey hotkey = Read(options, action);
            return hotkey.Enabled ? hotkey.ToString() : "";
        }

        // Only the main key: the badge tag and the footer are a few characters wide.
        internal static string ShortDisplay(Settings options, HotkeyAction action)
        {
            Hotkey hotkey = Read(options, action);
            if (!hotkey.Enabled) return "—";
            return hotkey.Key;
        }

        internal static void Write(Settings options, HotkeyAction action, Hotkey hotkey)
        {
            Entry(options, action).Value = Canonical(hotkey).ToString();
            options.Config.Save();
        }

        internal static string Reset(Settings options, HotkeyAction action)
        {
            Hotkey candidate = Hotkey.Parse(Default(action));
            string problem = Check(options, action, candidate);
            if (problem.Length > 0) return problem;
            Write(options, action, candidate);
            return "";
        }

        internal static string Default(HotkeyAction action)
        {
            if (action == HotkeyAction.Comment) return "F2";
            if (action == HotkeyAction.SortRecent) return "F3";
            return "Tab";
        }

        // KeyCode spells the key canonically ("F9", "Tab"), so store that form instead of
        // whatever the player typed in the config file.
        internal static Hotkey Canonical(Hotkey hotkey)
        {
            if (!hotkey.Enabled) return Hotkey.From(hotkey.Ctrl, hotkey.Alt, hotkey.Shift, Hotkey.Disabled);
            KeyCode key = Resolve(hotkey);
            if (key == KeyCode.None) return Hotkey.From(hotkey.Ctrl, hotkey.Alt, hotkey.Shift, Hotkey.Disabled);
            return Hotkey.From(hotkey.Ctrl, hotkey.Alt, hotkey.Shift, key.ToString());
        }

        internal static KeyCode Resolve(Hotkey hotkey)
        {
            if (!hotkey.Enabled) return KeyCode.None;
            int number;
            // Enum.TryParse accepts "13" for KeyCode.Return; a hand-edited config must not
            // turn a numeric string into an unrelated key.
            if (int.TryParse(hotkey.Key, out number)) return KeyCode.None;
            KeyCode key;
            return Enum.TryParse(hotkey.Key, true, out key) ? key : KeyCode.None;
        }

        internal static bool Down(Settings options, HotkeyAction action)
        {
            if (Capturing || GameAccess.Typing) return false;
            Hotkey hotkey = Read(options, action);
            if (!hotkey.Enabled) return false;
            if (CtrlHeld() != hotkey.Ctrl || AltHeld() != hotkey.Alt || ShiftHeld() != hotkey.Shift) return false;
            return Input.GetKeyDown(Resolve(hotkey));
        }

        private static bool CtrlHeld() { return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl); }
        private static bool AltHeld() { return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt); }
        private static bool ShiftHeld() { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }

        // Why a candidate cannot be stored; empty means it can. Clearing is always allowed,
        // so a player can drop a shortcut they cannot press.
        internal static string Check(Settings options, HotkeyAction action, Hotkey candidate)
        {
            if (!candidate.Enabled) return "";
            KeyCode key = Resolve(candidate);
            if (key == KeyCode.None) return "无法识别的按键。";
            if (key == KeyCode.Escape) return "Esc 用于取消，不能作为热键。";
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter) return "回车用于确认，不能作为热键。";
            if (key == KeyCode.LeftControl || key == KeyCode.RightControl || key == KeyCode.LeftAlt ||
                key == KeyCode.RightAlt || key == KeyCode.LeftShift || key == KeyCode.RightShift)
                return "修饰键不能单独使用，请再加一个键。";
            Hotkey canonical = Canonical(candidate);
            foreach (HotkeyAction other in new[] { HotkeyAction.Panel, HotkeyAction.Comment, HotkeyAction.SortRecent })
                if (other != action && string.Equals(Read(options, other).ToString(), canonical.ToString(),
                    StringComparison.OrdinalIgnoreCase))
                    return "与「" + Label(other) + "」重复，请换一个。";
            return "";
        }

        internal static string Label(HotkeyAction action)
        {
            if (action == HotkeyAction.Comment) return "存档备注";
            if (action == HotkeyAction.SortRecent) return "最近排序";
            return "面板";
        }
    }
}
