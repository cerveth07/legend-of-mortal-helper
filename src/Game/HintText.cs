using MortalHelper.Core;

namespace MortalHelper.Game
{
    // UI-independent rendering so inline and hover hints share the same names.
    internal static class HintText
    {
        private static string L(UiLang language, string text) => UiLanguage.Translate(text, language);
        internal static string Condition(string condition, UiLang language)
        {
            if (string.IsNullOrEmpty(condition)) return L(language, "条件（无法静态确定）");
            string named = System.Text.RegularExpressions.Regex.Replace(condition,
                @"分支 ([A-Za-z0-9_./:-]+) = (-?\d+)", match =>
                    HintNames.Checkpoint(match.Groups[1].Value, "Switch") + " = " + match.Groups[2].Value);
            return HintNames.Natural(named, language);
        }

        // One effect, in the words a player reads. Traditional names come from the game;
        // the overlay converts to the panel language, the inline summary to the game's.
        internal static string Effect(EffectCall effect, bool rich, UiLang language)
        {
            string[] a = effect.Arguments;
            if (a.Length == 0) return "";
            switch (effect.Method)
            {
                case "Player":
                    return Signed(GameNames.StatName(a[0]), a.Length > 1 ? a[1] : "", rich);
                case "SetPlayer":
                    return GameNames.StatName(a[0]) + " = " + (a.Length > 1 ? HintNames.StatValue(a[0], a[1]) : "?");
                case "Character":
                    return Signed(HintNames.Character(a[0]) + L(language, "好感"), a.Length > 1 ? a[1] : "", rich);
                case "SetCharacter":
                    return HintNames.Character(a[0]) + L(language, "好感") + " = " + (a.Length > 1 ? a[1] : "?");
                case "AddBook":
                    return L(language, "获得书籍：") + GameNames.ItemName(1, a[0]) + AmountText(a);
                case "AddMisc":
                    return L(language, "获得物品：") + GameNames.ItemName(2, a[0]) + AmountText(a);
                case "AddSpecial":
                    return L(language, "获得特殊物品：") + GameNames.ItemName(3, a[0]) + AmountText(a);
                case "RemoveBook":
                    return L(language, "失去书籍：") + GameNames.ItemName(1, a[0]);
                case "RemoveMisc":
                    return L(language, "失去物品：") + GameNames.ItemName(2, a[0]);
                case "RemoveSpecial":
                    return L(language, "失去特殊物品：") + GameNames.ItemName(3, a[0]);
                case "SetFlag":
                    return L(language, "剧情标记 ") + HintNames.Flag(a[0]) + " = " + (a.Length > 1 ? a[1] : "?");
                case "AddFlag":
                    return Signed(L(language, "剧情标记 ") + HintNames.Flag(a[0]), a.Length > 1 ? a[1] : "", rich);
                case "Mission":
                    return L(language, "任务：") + HintNames.Mission(a[0], a.Length > 1 ? a[1] : null);
                case "AddTalent":
                    return Signed(L(language, "天赋 ") + HintNames.Talent(a[0]), a.Length > 1 ? a[1] : "", rich);
                default:
                    return "";
            }
        }

        private static string AmountText(string[] a)
        {
            int value;
            if (a.Length < 2 || !int.TryParse(a[1], out value) || value == 1) return "";
            return " ×" + value;
        }

        private static string Signed(string name, string value, bool rich)
        {
            int number;
            if (!int.TryParse(value, out number) || number == 0) return name + " 不变";
            string sign = number > 0 ? "+" + number : number.ToString();
            if (!rich) return name + " " + sign;
            string color = number > 0 ? "#7FD08A" : "#E8837C";
            return name + " <color=" + color + ">" + sign + "</color>";
        }

    }
}
