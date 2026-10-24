using System;
using System.Globalization;

namespace MortalHelper.Core
{
    public enum RollPhase { Closed, Input, Committed, Resolved }

    // Owns intent for exactly one visible native dice interaction. No Unity dependency.
    public sealed class InteractionSession
    {
        public RollPhase Phase { get; private set; }
        public long Generation { get; private set; }
        public int? RequestedPoint { get; private set; }
        public int? RequestedOption { get; private set; }
        public bool HasOverride => RequestedPoint.HasValue || RequestedOption.HasValue;

        public void Open()
        {
            Generation++;
            ClearIntent();
            Phase = RollPhase.Input;
        }

        public void Close()
        {
            Generation++;
            ClearIntent();
            Phase = RollPhase.Closed;
        }

        public bool Rearm(bool nativeAllowsReroll)
        {
            if (Phase != RollPhase.Resolved || !nativeAllowsReroll) return false;
            ClearIntent();
            Phase = RollPhase.Input;
            return true;
        }

        public bool Commit(long generation, string point, int minimum, int maximum,
                           int? option, int optionCount, out string error)
        {
            error = null;
            if (generation != Generation || Phase != RollPhase.Input)
            { error = "本次判定已经提交或关闭。"; return false; }
            int? parsed;
            if (option.HasValue)
            {
                if (option.Value < 1 || option.Value > optionCount)
                { error = "选项已经失效。"; return false; }
                parsed = null;
            }
            else if (!ParsePoint(point, minimum, maximum, out parsed))
            { error = "请输入本次范围内的整数，或清空以自然掷骰。"; return false; }
            RequestedPoint = parsed;
            RequestedOption = option;
            Phase = RollPhase.Committed;
            return true;
        }

        public void Resolved()
        {
            if (Phase == RollPhase.Committed) Phase = RollPhase.Resolved;
            ClearIntent();
        }

        public static bool ParsePoint(string text, int minimum, int maximum, out int? point)
        {
            point = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            int value;
            if (minimum > maximum || !int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out value) || value < minimum || value > maximum) return false;
            point = value;
            return true;
        }

        private void ClearIntent() { RequestedPoint = null; RequestedOption = null; }
    }
}
