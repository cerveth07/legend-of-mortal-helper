using System;
using System.Collections.Generic;
using System.Text;
using Mortal.Core;
using Mortal.Story;
using MortalHelper.Core;

namespace MortalHelper.Game
{
    // What one named condition asks for, read from the game's own condition assets.
    internal sealed class ConditionReport
    {
        internal string Name = "";
        internal bool Known;       // the named condition was found and its items are readable
        internal bool? Satisfied;  // null when a random or nested item makes it undecidable
        internal readonly List<string> Lines = new List<string>();
        internal bool Single;      // exactly one stat comparison: the only settable shape
        internal int Comparisons;
        internal bool CanApply;
        internal string Block = "";
        internal GameStat Stat;
        internal int Needed;
        internal string StatLabel = "";
    }

    // Read-only interpretation of the named condition checkpoints, plus the one narrow
    // write path that can satisfy a simple one.
    //
    // Nothing here calls ConditionResultData.Execute() or SwitchResultData.Execute():
    // Execute() bumps the asset's own _count and evaluating a 隨機值 item draws from the
    // game's RNG. Both would change game state just by showing a tooltip, and the RNG
    // draw would desync the anytime-save replay.
    internal static class OptionConditions
    {
        private const int MaximumDepth = 3;
        private const int MaximumItems = 24;

        internal static ConditionReport Describe(OptionRequirement requirement)
        {
            var report = new ConditionReport { Name = requirement == null ? "" : requirement.Name };
            if (requirement == null || string.IsNullOrEmpty(requirement.Name)) return report;
            try
            {
                if (requirement.Kind == RequirementKind.Switch)
                {
                    report.Lines.Add("检查点 " + requirement.Name);
                    report.Block = "检查点类条件不能一键满足。";
                    return report;
                }
                if (requirement.Kind == RequirementKind.Position)
                {
                    report.Lines.Add("地点检查 " + requirement.Name);
                    report.Block = "地点类条件不能一键满足。";
                    return report;
                }
                CheckPointManager manager = CheckPointManager.Instance;
                if (manager == null)
                {
                    report.Block = "条件数据尚未就绪。";
                    return report;
                }
                ConditionResultData data = Find(manager, requirement.Name);
                if (data == null)
                {
                    report.Lines.Add("条件 " + requirement.Name);
                    report.Block = "游戏里找不到这个条件的定义。";
                    return report;
                }
                report.Known = true;
                report.Satisfied = State(data, report, 0);
                report.Single = report.Comparisons == 1;
                if (report.Lines.Count == 0) report.Lines.Add("无条件（视为成立）");
                if (!report.Single && report.Block.Length == 0)
                    report.Block = report.Satisfied == null ? "含随机或嵌套判定，不能一键满足。"
                        : "复合条件（多项或含且/或），需手动修改。";
                if (report.Single && report.Stat == null && report.Block.Length == 0)
                    report.Block = "只有单一属性条件支持一键设置。";
            }
            catch (Exception error)
            {
                report.Lines.Clear();
                report.Lines.Add("条件读取失败：" + error.GetBaseException().Message);
                report.Block = "条件读取失败，不能一键满足。";
                report.Satisfied = null;
            }
            return report;
        }

        // The game looks conditions up by asset name; so do we, without executing them.
        private static ConditionResultData Find(CheckPointManager manager, string name)
        {
            object config = GameAccess.Field<object>(manager, "_condition");
            if (config == null) return null;
            var list = GameAccess.Field<object>(config, "_list") as System.Collections.IEnumerable;
            if (list == null) return null;
            foreach (object entry in list)
            {
                var data = entry as ConditionResultData;
                if (data != null && data.name == name) return data;
            }
            return null;
        }

        // ConditionResultData.Execute() returns true when any item holds (OR).
        private static bool? State(ConditionResultData data, ConditionReport report, int depth)
        {
            if (data == null || depth > MaximumDepth) return null;
            List<ConditionResultItem> items = GameAccess.Field<List<ConditionResultItem>>(data, "_items");
            if (items == null || items.Count == 0) return true;
            bool? any = false;
            bool sole = items.Count == 1;
            for (int i = 0; i < items.Count && report.Lines.Count < MaximumItems; i++)
            {
                bool? state = ItemState(items[i], report, depth, sole);
                any = Or(any, state);
            }
            return any;
        }

        // ConditionResultItem.Check() applies its logic operator over its own comparisons.
        private static bool? ItemState(ConditionResultItem item, ConditionReport report, int depth, bool soleItem)
        {
            if (item == null) return null;
            LogicOperatorType logic = GameAccess.Field<LogicOperatorType>(item, "_logicOp");
            List<StatCompareItem> entries = GameAccess.Field<List<StatCompareItem>>(item, "_items");
            if (entries == null || entries.Count == 0) return null;
            bool? all = logic == LogicOperatorType.OR ? (bool?)false : true;
            for (int i = 0; i < entries.Count && report.Lines.Count < MaximumItems; i++)
            {
                bool? holds = Compare(entries[i], report, soleItem && entries.Count == 1);
                if (logic == LogicOperatorType.OR) all = Or(all, holds);
                else all = And(all, holds);
            }
            return all;
        }

        private static bool? Compare(StatCompareItem compare, ConditionReport report, bool singleEntry)
        {
            if (compare == null) return null;
            StatCompareType op = GameAccess.Field<StatCompareType>(compare, "_compareOp");
            StatGroupVariable left = GameAccess.Field<StatGroupVariable>(compare, "_value1");
            StatGroupVariable right = GameAccess.Field<StatGroupVariable>(compare, "_value2");
            if (left == null || right == null) return null;
            // A random reference would advance the game's RNG when read; show it, do not read it.
            bool readable = !Random(left, 0) && !Random(right, 0);
            int current = readable ? left.GetResult() : 0;
            int target = readable ? right.GetResult() : 0;
            bool? holds = readable ? Holds(op, current, target) : (bool?)null;
            report.Comparisons++;
            bool first = report.Lines.Count == 0;
            string prefix = first || singleEntry ? "" : (op == StatCompareType.Equal ? "" : "");
            var text = new StringBuilder();
            text.Append(prefix).Append(Describe(left, 0, report, singleEntry && readable));
            text.Append(' ').Append(Symbol(op)).Append(' ');
            text.Append(Describe(right, 0, report, false));
            text.Append("（现 ").Append(readable ? current.ToString() : "?").Append(
                holds == true ? " √）" : holds == false ? " ×）" : " ?）");
            report.Lines.Add(text.ToString());
            if (singleEntry && report.Stat != null && holds == false)
            {
                int needed = Needed(op, target);
                if (!IsConstant(right))
                    report.Block = "比较的另一侧不是固定数值，不能一键满足。";
                else if (needed == int.MinValue)
                    report.Block = "「≠」条件不能一键满足。";
                else if (needed < report.Stat.Min || needed > report.Stat.Max)
                    report.Block = "需要的值超出该属性范围（" + report.Stat.Min + "～" + report.Stat.Max + "）。";
                else
                {
                    report.CanApply = true;
                    report.Needed = needed;
                    report.Block = "";
                }
            }
            return holds;
        }

        private static bool? Or(bool? left, bool? right)
        {
            if (left == true || right == true) return true;
            if (left == null || right == null) return null;
            return false;
        }

        private static bool? And(bool? left, bool? right)
        {
            if (left == false || right == false) return false;
            if (left == null || right == null) return null;
            return true;
        }

        private static bool Holds(StatCompareType op, int current, int target)
        {
            switch (op)
            {
                case StatCompareType.Equal: return current == target;
                case StatCompareType.NotEqual: return current != target;
                case StatCompareType.Less: return current < target;
                case StatCompareType.LessEqual: return current <= target;
                case StatCompareType.Greater: return current > target;
                case StatCompareType.GreaterEqual: return current >= target;
                default: return false;
            }
        }

        private static int Needed(StatCompareType op, int target)
        {
            switch (op)
            {
                case StatCompareType.Equal: return target;
                case StatCompareType.LessEqual: return target;
                case StatCompareType.GreaterEqual: return target;
                case StatCompareType.Less: return target - 1;
                case StatCompareType.Greater: return target + 1;
                default: return int.MinValue;
            }
        }

        private static string Symbol(StatCompareType op)
        {
            switch (op)
            {
                case StatCompareType.Equal: return "=";
                case StatCompareType.NotEqual: return "≠";
                case StatCompareType.Less: return "<";
                case StatCompareType.LessEqual: return "≤";
                case StatCompareType.Greater: return ">";
                case StatCompareType.GreaterEqual: return "≥";
                default: return "?";
            }
        }

        // Every comparison is rendered; only a lone stat against a constant can be written.
        private static string Describe(StatGroupVariable group, int depth, ConditionReport report, bool simple)
        {
            if (group == null || depth > MaximumDepth) return "?";
            List<StatValueReference> references = GameAccess.Field<List<StatValueReference>>(group, "_value");
            if (references == null || references.Count == 0) return "0";
            StatGroupType kind = GameAccess.Field<StatGroupType>(group, "_groupType");
            var parts = new List<string>();
            for (int i = 0; i < references.Count && i < 6; i++)
            {
                if (references[i] == null) continue;
                parts.Add(Describe(reference: references[i], depth: depth,
                    report: report, simple: simple && references.Count == 1 && kind == StatGroupType.Sum));
            }
            string separator = kind == StatGroupType.Max ? " 或 " : kind == StatGroupType.Min ? " 与 " : "＋";
            return string.Join(separator, parts.ToArray());
        }

        private static string Describe(StatValueReference reference, int depth, ConditionReport report, bool simple)
        {
            if (reference == null) return "?";
            StatCheckType type = GameAccess.Field<StatCheckType>(reference, "_checkType");
            switch (type)
            {
                case StatCheckType.常數:
                    return GameAccess.Field<int>(reference, "_constant").ToString();
                case StatCheckType.角色數值:
                    {
                        GameStat stat = GameAccess.Field<GameStat>(reference, "_stat");
                        if (simple && stat != null && NumericEdits.Stats.Contains(stat.StatType.ToString()))
                        {
                            // Write through the player's own instance: the condition may reference
                            // the same asset, but the stat list is what the write path verifies.
                            report.Stat = Live(stat);
                            report.StatLabel = GameNames.StatName(stat);
                        }
                        return GameNames.StatName(stat);
                    }
                case StatCheckType.好感度:
                    {
                        RelationshipStat relationship = GameAccess.Field<RelationshipStat>(reference, "_relationship");
                        return relationship == null ? "好感" : "好感 " + relationship.Type;
                    }
                case StatCheckType.旗標:
                    {
                        FlagData flag = GameAccess.Field<FlagData>(reference, "_flag");
                        if (flag == null) return "剧情标记";
                        string note = flag.DevNote;
                        return string.IsNullOrEmpty(note) ? "剧情标记 " + flag.name
                            : "剧情标记 " + flag.name + "（" + note + "）";
                    }
                case StatCheckType.書籍:
                    return "书籍 " + GameNames.ItemName(GameAccess.Field<ItemData>(reference, "_book"));
                case StatCheckType.雜物:
                    return "物品 " + GameNames.ItemName(GameAccess.Field<ItemData>(reference, "_misc"));
                case StatCheckType.貴重品:
                    return "贵重品 " + GameNames.ItemName(GameAccess.Field<ItemData>(reference, "_specialItem"));
                case StatCheckType.隨機值:
                    return "随机 " + GameAccess.Field<int>(reference, "_randomMin") + "～" +
                        GameAccess.Field<int>(reference, "_randomMax");
                case StatCheckType.數值群組:
                    return Describe(GameAccess.Field<StatGroupVariable>(reference, "_statGroup"), depth + 1, report, false);
                case StatCheckType.遊戲時間: return "游戏时间";
                case StatCheckType.條件設定: return "子条件";
                default: return "其它值";
            }
        }

        private static GameStat Live(GameStat stat)
        {
            if (stat == null) return null;
            try
            {
                var manager = PlayerStatManagerData.Instance;
                if (manager != null && manager.Stats != null)
                {
                    GameStat live = manager.Stats.Get(stat.StatType);
                    if (live != null) return live;
                }
            }
            catch (Exception) { }
            return stat;
        }

        // A random reference makes the whole comparison unreadable without side effects.
        private static bool Random(StatGroupVariable group, int depth)
        {
            if (group == null || depth > MaximumDepth) return true;
            List<StatValueReference> references = GameAccess.Field<List<StatValueReference>>(group, "_value");
            if (references == null) return true;
            foreach (StatValueReference reference in references)
            {
                if (reference == null) return true;
                StatCheckType type = GameAccess.Field<StatCheckType>(reference, "_checkType");
                if (type == StatCheckType.隨機值 || type == StatCheckType.遊戲時間 ||
                    type == StatCheckType.條件設定) return true;
                if (type == StatCheckType.數值群組 &&
                    Random(GameAccess.Field<StatGroupVariable>(reference, "_statGroup"), depth + 1)) return true;
            }
            return false;
        }

        private static bool IsConstant(StatGroupVariable group)
        {
            if (group == null) return false;
            List<StatValueReference> references = GameAccess.Field<List<StatValueReference>>(group, "_value");
            if (references == null || references.Count != 1 || references[0] == null) return false;
            return GameAccess.Field<StatCheckType>(references[0], "_checkType") == StatCheckType.常數;
        }

        internal sealed class ApplyResult
        {
            internal bool Wrote;
            internal bool Satisfied;
            internal string Message = "";
        }

        // Writes the value a single stat comparison needs, through the same journaled path
        // the numeric page uses. It never touches the option row or the condition asset; the
        // caller decides whether the row may be opened in place.
        internal static ApplyResult Apply(ConditionReport report, OptionRequirement requirement)
        {
            var result = new ApplyResult();
            if (report == null || !report.CanApply || report.Stat == null)
            {
                result.Message = report != null && report.Block.Length > 0 ? report.Block : "这个条件不能一键满足。";
                return result;
            }
            int before = report.Stat.Value;
            try { NumericEdits.SetStat(report.Stat, report.Needed); }
            catch (Exception error)
            {
                result.Message = "设置失败：" + error.GetBaseException().Message;
                return result;
            }
            result.Wrote = true;
            int after = report.Stat.Value;
            string label = report.StatLabel.Length > 0 ? report.StatLabel : GameNames.StatName(report.Stat);
            result.Message = "已把「" + label + "」从 " + before + " 改为 " + after +
                (report.Needed == after ? "" : "（目标 " + report.Needed + "）") + "。";
            try
            {
                ConditionReport again = Describe(requirement);
                result.Satisfied = again.Satisfied == true;
                if (result.Satisfied) result.Message += " 条件已成立。";
                else if (again.Satisfied == null) result.Message += " 这个条件含随机判定，无法确认是否成立。";
                else result.Message += " 条件仍未成立，需要重新打开选项。";
            }
            catch (Exception) { }
            return result;
        }
    }
}



