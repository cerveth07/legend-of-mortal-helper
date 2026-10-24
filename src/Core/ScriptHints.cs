using System;
using System.Collections.Generic;
using System.Text;

namespace MortalHelper.Core
{
    // What an option does, in the categories a player can read. The parser only
    // classifies; turning a key into a Chinese name is the game layer's job.
    public enum EffectKind { Stat, Relation, ItemGain, ItemLoss, Flag, Quest, Talent }

    public sealed class EffectCall
    {
        public string Method;
        public string[] Arguments;
        public EffectKind Kind;
        // null when the call always runs; "" when it sits behind a condition the
        // reader could not resolve; otherwise a short label such as "分支 X = 2".
        public string Condition;
    }

    public sealed class BranchHint
    {
        public string Id;
        public readonly List<EffectCall> Effects = new List<EffectCall>();
        // Branch points and story jumps the option leads to. These calls are not
        // stat changes, so they are collected separately.
        public readonly List<string> Consequences = new List<string>();
        // True when a call was skipped because it sits behind a condition or its
        // arguments are computed: the list is then a lower bound, never a guess.
        public bool Incomplete;
    }

    // Deliberately a restricted reader, never a Lua evaluator. Unknown or computed
    // code is skipped instead of ending the scan: real scripts open an option with
    // presentation calls and only then change stats. Calls inside conditions are
    // never claimed as definite.
    public static class ScriptHints
    {
        private const int MaximumEffects = 16;
        private const int MaximumConsequences = 6;

        private enum Kind { Word, Number, Text, Symbol, Marker, End }
        private struct Token
        {
            public Kind Kind;
            public string Text;
            public Token(Kind kind, string text) { Kind = kind; Text = text; }
        }

        private static readonly Dictionary<string, EffectKind> Effects = new Dictionary<string, EffectKind>(StringComparer.Ordinal) {
            { "Player", EffectKind.Stat }, { "SetPlayer", EffectKind.Stat },
            { "Character", EffectKind.Relation }, { "SetCharacter", EffectKind.Relation },
            { "AddBook", EffectKind.ItemGain }, { "AddMisc", EffectKind.ItemGain }, { "AddSpecial", EffectKind.ItemGain },
            { "RemoveBook", EffectKind.ItemLoss }, { "RemoveMisc", EffectKind.ItemLoss },
            { "RemoveSpecial", EffectKind.ItemLoss },
            { "SetFlag", EffectKind.Flag }, { "AddFlag", EffectKind.Flag },
            { "Mission", EffectKind.Quest }, { "AddTalent", EffectKind.Talent }
        };

        // Blocks are tracked as a stack so a call inside "if ... then" can still be
        // reported: it is labelled with the first resolvable condition (a branch
        // switch such as `local r = checkpointmanager.Switch("X"); if r == 2 then`)
        // and never presented as a definite effect. Loops and function bodies stay
        // opaque: their calls may run zero or many times, so they are not listed.
        private enum BlockKind { If, Opaque }
        private struct Block { public BlockKind Kind; public string Condition; }

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
            var blocks = new List<Block>();
            var switches = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < tokens.Count;)
            {
                Token t = tokens[i++];
                if (t.Kind == Kind.Marker)
                {
                    // A marker starts the option's own branch: block state belongs to
                    // the enclosing menu, not to whatever came before.
                    current = new BranchHint { Id = t.Text };
                    if (result.ContainsKey(t.Text)) duplicates.Add(t.Text);
                    else result.Add(t.Text, current);
                    active = true;
                    blocks.Clear();
                    switches.Clear();
                    continue;
                }
                if (!active || t.Kind == Kind.End) continue;
                if (t.Text == ";") continue;
                if (t.Kind == Kind.Word)
                {
                    if (t.Text == "local") { ReadSwitchAlias(tokens, ref i, switches); continue; }
                    if (t.Text == "if" || t.Text == "elseif")
                    {
                        // At block depth zero this else-if belongs to the enclosing menu
                        // condition, so the option's own branch has ended.
                        if (t.Text == "elseif" && blocks.Count == 0) { active = false; continue; }
                        string condition = ReadCondition(tokens, ref i, switches);
                        if (t.Text == "if") blocks.Add(new Block { Kind = BlockKind.If, Condition = condition });
                        else if (blocks[blocks.Count - 1].Kind == BlockKind.If)
                            blocks[blocks.Count - 1] = new Block { Kind = BlockKind.If, Condition = condition };
                        continue;
                    }
                    if (t.Text == "else")
                    {
                        if (blocks.Count == 0) { active = false; continue; }
                        if (blocks[blocks.Count - 1].Kind == BlockKind.If)
                            blocks[blocks.Count - 1] = new Block { Kind = BlockKind.If, Condition = "" };
                        continue;
                    }
                    if (t.Text == "for" || t.Text == "while" || t.Text == "function" || t.Text == "repeat")
                    {
                        blocks.Add(new Block { Kind = BlockKind.Opaque, Condition = null });
                        continue;
                    }
                    if (t.Text == "end" || t.Text == "until")
                    {
                        if (blocks.Count > 0) blocks.RemoveAt(blocks.Count - 1);
                        else active = false;
                        continue;
                    }
                    if (i + 1 >= tokens.Count || tokens[i].Kind != Kind.Symbol || tokens[i].Text != ".") continue;
                    string owner = t.Text;
                    string method = tokens[i + 1].Text;
                    i += 2;
                    // Not a call: a field assignment such as "sayoptions.waitforinput = true".
                    if (i >= tokens.Count || tokens[i].Kind != Kind.Symbol || tokens[i].Text != "(") continue;
                    i++;
                    List<string> arguments;
                    bool literal = ReadArguments(tokens, ref i, out arguments);
                    if (current == null) continue;
                    EffectKind kind;
                    if (owner == "statmodifymanager" && Effects.TryGetValue(method, out kind))
                    {
                        if (!literal || HasOpaque(blocks)) { current.Incomplete = true; continue; }
                        if (current.Effects.Count >= MaximumEffects) { current.Incomplete = true; continue; }
                        string condition = ConditionOf(blocks);
                        // A conditional call is listed, but the list can never be
                        // presented as the complete set of outcomes.
                        if (condition != null) current.Incomplete = true;
                        current.Effects.Add(new EffectCall
                        {
                            Method = method, Arguments = arguments.ToArray(), Kind = kind, Condition = condition
                        });
                        continue;
                    }
                    // story jumps (AddStory included) are destinations, not stat changes
                    if (blocks.Count > 0 || !literal) continue;
                    string label = Consequence(owner, method, arguments);
                    if (label == null) continue;
                    if (current.Consequences.Count >= MaximumConsequences) { current.Incomplete = true; continue; }
                    if (!current.Consequences.Contains(label)) current.Consequences.Add(label);
                    continue;
                }
            }
            foreach (string id in duplicates) result.Remove(id);
            return result;
        }

        private static bool HasOpaque(List<Block> blocks)
        {
            for (int i = 0; i < blocks.Count; i++) if (blocks[i].Kind == BlockKind.Opaque) return true;
            return false;
        }

        // null means "runs unconditionally"; "" means "conditional, label unknown".
        private static string ConditionOf(List<Block> blocks)
        {
            return blocks.Count == 0 ? null : blocks[blocks.Count - 1].Condition;
        }

        // Reads "if/elseif <expr> then", skips the expression, and resolves the one
        // shape the game actually uses: NAME == NUMBER where NAME aliases a native
        // branch switch. Anything else stays unknown instead of being guessed.
        private static string ReadCondition(List<Token> tokens, ref int index, Dictionary<string, string> switches)
        {
            int start = index;
            int then = -1;
            for (int k = index; k < tokens.Count && k < index + 64; k++)
                if (tokens[k].Kind == Kind.Word && tokens[k].Text == "then") { then = k; break; }
            if (then < 0) { index = tokens.Count; return ""; }
            index = then + 1;
            // The scanner emits one symbol per character, so "==" is two "=" tokens.
            if (then - start == 4 && tokens[start].Kind == Kind.Word &&
                Symbol(tokens[start + 1], "=") && Symbol(tokens[start + 2], "=") &&
                tokens[start + 3].Kind == Kind.Number)
            {
                string source;
                if (switches.TryGetValue(tokens[start].Text, out source))
                    return "分支 " + source + " = " + tokens[start + 3].Text;
            }
            return "";
        }

        // `local NAME = checkpointmanager.Switch("X")` — the name is meaningless to a
        // player, the switch id is what the branch point is called in the story.
        private static void ReadSwitchAlias(List<Token> tokens, ref int index, Dictionary<string, string> switches)
        {
            int i = index;
            if (i + 7 >= tokens.Count) return;
            if (tokens[i].Kind != Kind.Word) return;
            if (!Symbol(tokens[i + 1], "=")) return;
            if (tokens[i + 2].Kind != Kind.Word || tokens[i + 2].Text != "checkpointmanager") return;
            if (!Symbol(tokens[i + 3], ".")) return;
            if (tokens[i + 4].Kind != Kind.Word || tokens[i + 4].Text != "Switch") return;
            if (!Symbol(tokens[i + 5], "(")) return;
            if (tokens[i + 6].Kind != Kind.Text) return;
            if (!Symbol(tokens[i + 7], ")")) return;
            switches[tokens[i].Text] = tokens[i + 6].Text;
            // Deliberately does not advance: the switch call itself is still scanned as
            // a consequence, so "分支点 X" stays in the destination list.
        }

        private static bool Symbol(Token token, string text)
        {
            return token.Kind == Kind.Symbol && token.Text == text;
        }

        // The three calls the game uses to move the story sideways.
        private static string Consequence(string owner, string method, List<string> arguments)
        {
            if (arguments.Count == 0) return null;
            string first = arguments[0];
            if (owner == "checkpointmanager" && method == "Switch") return "分支点 " + first;
            // AddStory records a LegendInfo entry (the "纪事" text kept in the save);
            // SetNextScript actually jumps to the next script file. Both used to read
            // "剧情", which made a recorded entry look like a destination.
            if (owner == "luamanager" && method == "SetNextScript") return "下一段 " + first;
            if (owner == "luamanager" && method == "ChangeScene") return "场景 " + first;
            if (owner == "statmodifymanager" && method == "AddStory") return "记录 " + first;
            return null;
        }

        // Literal arguments only. A computed argument makes the whole call unreadable,
        // so the caller flags the option as a lower bound instead of showing a guess.
        private static bool ReadArguments(List<Token> tokens, ref int index, out List<string> arguments)
        {
            arguments = new List<string>();
            while (index < tokens.Count)
            {
                if (tokens[index].Kind == Kind.Symbol && tokens[index].Text == ")") { index++; return true; }
                bool negative = tokens[index].Kind == Kind.Symbol && tokens[index].Text == "-";
                if (negative) index++;
                if (index >= tokens.Count) return false;
                Token argument = tokens[index];
                bool literal = argument.Kind == Kind.Number ||
                    (!negative && (argument.Kind == Kind.Text || argument.Text == "true" ||
                        argument.Text == "false" || argument.Text == "nil"));
                if (!literal || arguments.Count >= 16) { SkipToClose(tokens, ref index); return false; }
                arguments.Add((negative ? "-" : "") + argument.Text);
                index++;
                if (index >= tokens.Count) return false;
                if (tokens[index].Kind == Kind.Symbol && tokens[index].Text == ",") { index++; continue; }
                if (tokens[index].Kind == Kind.Symbol && tokens[index].Text == ")") { index++; return true; }
                SkipToClose(tokens, ref index);
                return false;
            }
            return false;
        }

        private static void SkipToClose(List<Token> tokens, ref int index)
        {
            int open = 1;
            while (index < tokens.Count)
            {
                Token t = tokens[index++];
                if (t.Kind == Kind.End) return;
                if (t.Kind != Kind.Symbol) continue;
                if (t.Text == "(") open++;
                else if (t.Text == ")" && --open <= 0) return;
            }
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
