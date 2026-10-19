using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MortalHelper.Core
{
    public sealed class EffectCall
    {
        public string Method;
        public string[] Arguments;
    }

    public sealed class BranchHint
    {
        public string Id;
        public readonly List<EffectCall> Effects = new List<EffectCall>();
        // Branch points and story jumps the option leads to. These calls are not
        // stat changes, so they get their own scan of the option block.
        public readonly List<string> Consequences = new List<string>();
        public bool Incomplete;
    }

    // Deliberately a restricted reader, never a Lua evaluator. Unknown code closes
    // the current straight-line preview. Comments and quoted data cannot become code.
    public static class ScriptHints
    {
        private enum Kind { Word, Number, Text, Symbol, Marker, End }
        private struct Token
        {
            public Kind Kind;
            public string Text;
            public Token(Kind kind, string text) { Kind = kind; Text = text; }
        }

        public static Dictionary<string, BranchHint> Read(string source)
        {
            var result = new Dictionary<string, BranchHint>(StringComparer.Ordinal);
            if (source == null || source.Length > 2000000) return result;
            List<Token> tokens;
            try { tokens = Scan(source); }
            catch (FormatException) { return result; }
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            BranchHint current = null;
            bool active = false;
            for (int i = 0; i < tokens.Count;)
            {
                Token t = tokens[i++];
                if (t.Kind == Kind.Marker)
                {
                    current = new BranchHint { Id = t.Text };
                    if (result.ContainsKey(t.Text)) duplicates.Add(t.Text);
                    else result.Add(t.Text, current);
                    active = true;
                    continue;
                }
                if (!active || t.Kind == Kind.End) continue;
                if (t.Text == ";") continue;
                if (t.Kind == Kind.Word && (t.Text == "end" || t.Text == "else" || t.Text == "elseif"))
                { active = false; continue; }
                if (t.Kind != Kind.Word || t.Text != "statmodifymanager")
                { current.Incomplete = true; active = false; continue; }
                if (!Take(tokens, ref i, ".") || i >= tokens.Count || tokens[i].Kind != Kind.Word)
                { current.Incomplete = true; active = false; continue; }
                string method = tokens[i++].Text;
                var args = new List<string>();
                bool ok = Take(tokens, ref i, "(");
                if (ok && !Take(tokens, ref i, ")"))
                {
                    while (ok)
                    {
                        bool negative = Take(tokens, ref i, "-");
                        if (i >= tokens.Count) { ok = false; break; }
                        Token arg = tokens[i++];
                        bool literal = arg.Kind == Kind.Number || (!negative && (arg.Kind == Kind.Text ||
                            arg.Text == "true" || arg.Text == "false" || arg.Text == "nil"));
                        if (!literal || args.Count >= 16) { ok = false; break; }
                        args.Add((negative ? "-" : "") + arg.Text);
                        if (Take(tokens, ref i, ")")) break;
                        if (!Take(tokens, ref i, ",")) { ok = false; break; }
                    }
                }
                if (!ok || method == "GetDisplayTime") { current.Incomplete = true; active = false; }
                else current.Effects.Add(new EffectCall { Method = method, Arguments = args.ToArray() });
            }
            foreach (string id in duplicates) result.Remove(id);
            CollectConsequences(source, result);
            return result;
        }

        // Scans each option block for the three calls the game uses to move the story
        // sideways. Unknown call shapes are ignored rather than guessed at.
        private static void CollectConsequences(string source, Dictionary<string, BranchHint> result)
        {
            var marker = new Regex(@"(?m)^[ \t]*--[ \t]*index:([A-Za-z0-9_]{1,200})[ \t]*\r?$");
            var branch = new Regex("checkpointmanager\\.Switch\\(\\s*[\"']([^\"']*)", RegexOptions.IgnoreCase);
            var next = new Regex("luamanager\\.SetNextScript\\(\\s*[\"']([^\"']*)", RegexOptions.IgnoreCase);
            var story = new Regex("statmodifymanager\\.AddStory\\(\\s*[\"']([^\"']*)", RegexOptions.IgnoreCase);
            var matches = marker.Matches(source);
            for (int i = 0; i < matches.Count; i++)
            {
                BranchHint hint;
                if (!result.TryGetValue(matches[i].Groups[1].Value, out hint)) continue;
                int start = matches[i].Index + matches[i].Length;
                int end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
                string block = StripComments(source.Substring(start, Math.Max(0, end - start)));
                Collect(hint, branch, block, "分支点 ");
                Collect(hint, next, block, "剧情 ");
                Collect(hint, story, block, "剧情 ");
            }
        }

        // Commented-out calls must never show up as real consequences.
        private static string StripComments(string block)
        {
            block = Regex.Replace(block, "--\\[(=*)\\[.*?\\]\\1\\]", " ", RegexOptions.Singleline);
            return Regex.Replace(block, "--[^\n]*", " ");
        }

        private static void Collect(BranchHint hint, Regex pattern, string block, string prefix)
        {
            foreach (Match match in pattern.Matches(block))
            {
                if (hint.Consequences.Count >= 6) return;
                string text = prefix + match.Groups[1].Value;
                if (!hint.Consequences.Contains(text)) hint.Consequences.Add(text);
            }
        }

        private static bool Take(List<Token> items, ref int index, string value)
        {
            if (index >= items.Count || items[index].Kind == Kind.Text || items[index].Text != value) return false;
            index++;
            return true;
        }

        private static List<Token> Scan(string source)
        {
            var output = new List<Token>();
            for (int p = 0; p < source.Length;)
            {
                if (output.Count > 200000) throw new FormatException();
                char c = source[p];
                if (char.IsWhiteSpace(c)) { p++; continue; }
                if (c == '-' && p + 1 < source.Length && source[p + 1] == '-')
                {
                    int lineStart = p == 0 ? 0 : source.LastIndexOf('\n', p - 1) + 1;
                    bool standalone = true;
                    for (int before = lineStart; before < p; before++)
                        if (!char.IsWhiteSpace(source[before])) standalone = false;
                    p += 2;
                    string ignored;
                    if (LongText(source, ref p, out ignored)) continue;
                    int start = p;
                    while (p < source.Length && source[p] != '\n') p++;
                    string comment = source.Substring(start, p - start).Trim();
                    if (standalone && comment.StartsWith("index:", StringComparison.Ordinal))
                    {
                        string id = comment.Substring(6).Trim();
                        bool valid = id.Length > 0 && id.Length <= 200;
                        foreach (char v in id) if (!(char.IsLetterOrDigit(v) || v == '_')) valid = false;
                        if (valid) output.Add(new Token(Kind.Marker, id));
                    }
                    continue;
                }
                string longValue;
                if (LongText(source, ref p, out longValue)) { output.Add(new Token(Kind.Text, longValue)); continue; }
                if (c == '\'' || c == '"')
                {
                    char quote = c; p++;
                    var text = new StringBuilder(); bool closed = false;
                    while (p < source.Length)
                    {
                        c = source[p++];
                        if (c == quote) { closed = true; break; }
                        if (c == '\n' || c == '\r') throw new FormatException();
                        if (c == '\\')
                        {
                            if (p >= source.Length) throw new FormatException();
                            char escape = source[p++];
                            if (escape == 'n') c = '\n';
                            else if (escape == 'r') c = '\r';
                            else if (escape == 't') c = '\t';
                            else if (escape == '\\' || escape == '\'' || escape == '"') c = escape;
                            else throw new FormatException();
                        }
                        text.Append(c);
                        if (text.Length > 16384) throw new FormatException();
                    }
                    if (!closed) throw new FormatException();
                    output.Add(new Token(Kind.Text, text.ToString()));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = p++;
                    while (p < source.Length && (char.IsLetterOrDigit(source[p]) || source[p] == '_')) p++;
                    output.Add(new Token(Kind.Word, source.Substring(start, p - start))); continue;
                }
                if (c >= '0' && c <= '9')
                {
                    int start = p++;
                    while (p < source.Length && source[p] >= '0' && source[p] <= '9') p++;
                    output.Add(new Token(Kind.Number, source.Substring(start, p - start))); continue;
                }
                output.Add(new Token(Kind.Symbol, c.ToString())); p++;
            }
            output.Add(new Token(Kind.End, ""));
            return output;
        }

        private static bool LongText(string source, ref int p, out string value)
        {
            value = null;
            if (p >= source.Length || source[p] != '[') return false;
            int q = p + 1;
            while (q < source.Length && source[q] == '=') q++;
            if (q >= source.Length || source[q] != '[') return false;
            string close = "]" + new string('=', q - p - 1) + "]";
            int end = source.IndexOf(close, q + 1, StringComparison.Ordinal);
            if (end < 0 || end - q > 65536) throw new FormatException();
            value = source.Substring(q + 1, end - q - 1);
            p = end + close.Length;
            return true;
        }
    }
}
