using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MortalHelper.Core
{
    public enum RequirementKind { Condition, Switch, Position }

    // A row of a script's option table that is only offered while a named checkpoint
    // holds. Both texts are kept: the one shown when it holds, and the "~" variant
    // shown while it does not.
    public sealed class OptionRequirement
    {
        public RequirementKind Kind = RequirementKind.Condition;
        public string Name = "";
        public string TextKey = "";
        public string LockedKey = "";
        public bool HasLocked;
    }

    // Reads the generated option tables the way the game writes them:
    //   local X = checkpointmanager.Condition("Name")
    //   options[2] = X and "O_unlocked" or "~O_locked"
    // Nothing is evaluated here; the condition itself is looked up in the game.
    public static class ScriptOptions
    {
        private const int MaximumLines = 400000;
        private const int MaximumEntries = 20000;
        private const int MaximumName = 200;

        // "--[[", "--[=[", "--[==[" all close with a bracketed run, never with plain "]]" only.
        private static readonly Regex LongCommentEnd = new Regex(@"\]=*\]", RegexOptions.CultureInvariant);
        private static readonly Regex Definition = new Regex(
            "^[ \t]*local[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t]*=[ \t]*checkpointmanager[ \t]*\\.[ \t]*(Condition|Switch|Position)" +
            "[ \t]*\\([ \t]*\"([^\"]{1," + MaximumName + "})\"[ \t]*\\)[ \t\r]*$",
            RegexOptions.CultureInvariant);
        private static readonly Regex ConditionalRow = new Regex(
            "^[ \t]*[A-Za-z_][A-Za-z0-9_.]*[ \t]*\\[[ \t]*[0-9]+[ \t]*\\][ \t]*=[ \t]*" +
            "([A-Za-z_][A-Za-z0-9_]*)(?:[ \t]*==[ \t]*[0-9]+)?[ \t]+and[ \t]+\"([^\"]{1," + MaximumName + "})\"" +
            "[ \t]+or[ \t]+\"~([^\"]{1," + MaximumName + "})\"[ \t\r]*$",
            RegexOptions.CultureInvariant);

        public static Dictionary<string, OptionRequirement> Read(string source)
        {
            var result = new Dictionary<string, OptionRequirement>(StringComparer.Ordinal);
            if (source == null || source.Length > 2000000) return result;
            string[] lines = source.Split('\n');
            if (lines.Length > MaximumLines) return result;

            var variables = new Dictionary<string, OptionRequirement>(StringComparer.Ordinal);
            bool inLongComment = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (inLongComment)
                {
                    if (LongCommentEnd.IsMatch(line)) inLongComment = false;
                    continue;
                }
                string trimmed = line.TrimStart();
                // Only a comment line opens a long comment: "--[[" inside a story line is text.
                if (trimmed.StartsWith("--[", StringComparison.Ordinal))
                {
                    if (!LongCommentEnd.IsMatch(trimmed, 3)) inLongComment = true;
                    continue;
                }
                if (trimmed.StartsWith("--", StringComparison.Ordinal)) continue;
                Match definition = Definition.Match(line);
                if (definition.Success)
                {
                    string variable = definition.Groups[1].Value;
                    if (!variables.ContainsKey(variable))
                        variables.Add(variable, new OptionRequirement
                        {
                            Kind = Kind(definition.Groups[2].Value),
                            Name = definition.Groups[3].Value
                        });
                    continue;
                }
                Match row = ConditionalRow.Match(line);
                if (!row.Success) continue;
                OptionRequirement requirement;
                if (!variables.TryGetValue(row.Groups[1].Value, out requirement)) continue;
                if (result.Count >= MaximumEntries) break;
                string textKey = row.Groups[2].Value;
                string lockedKey = row.Groups[3].Value;
                // Rows built from "talk+key+0+0" belong to the walk-around menus and carry a
                // placeholder lock text ("x"); indexing them would only add noise.
                if (textKey.Length < 2 || lockedKey.Length < 2) continue;
                if (textKey.IndexOf('+') >= 0 || lockedKey.IndexOf('+') >= 0) continue;
                var entry = new OptionRequirement
                {
                    Kind = requirement.Kind,
                    Name = requirement.Name,
                    TextKey = textKey,
                    LockedKey = lockedKey,
                    HasLocked = true
                };
                if (!result.ContainsKey(entry.TextKey)) result.Add(entry.TextKey, entry);
                if (!result.ContainsKey(entry.LockedKey)) result.Add(entry.LockedKey, entry);
            }
            return result;
        }

        private static RequirementKind Kind(string api)
        {
            if (api == "Switch") return RequirementKind.Switch;
            if (api == "Position") return RequirementKind.Position;
            return RequirementKind.Condition;
        }
    }
}


