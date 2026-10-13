using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VRCast.Core
{
    /// <summary>
    /// 設定プリセット・まばたきの .anim 向けの YAML と JSON の相互変換。書き出しは JSON をブロック形式の YAML に、読み込みは手で書きやすい範囲の YAML
    /// （ブロック形式のマッピング・シーケンス、1 行の [ ] / { }、引用符の有無を問わない文字列、# のコメント）を JSON に直す。
    /// アンカー・タグ・複数行の文字列（| >）・複数行にまたがる引用符なしの文字列は扱わない（壊れたものとして false）。
    /// </summary>
    public static class SimpleYaml
    {
        // 字下げ 1 段分（空白の数）
        private const int IndentWidth = 2;

        // JSON の数値の書式（そのまま通せる）と、JSON では書けないが YAML の数値として読む書式（+1・.5・1.・先頭の 0 など）
        private static readonly Regex JsonNumber = new Regex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][-+]?[0-9]+)?$");
        private static readonly Regex LooseNumber = new Regex(@"^[-+]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][-+]?[0-9]+)?$");

        // 引用符なしでは文字列以外に読まれうる語（ほかの YAML ツールで開いても変わらないよう、YAML 1.1 の真偽値も引用符で囲む）
        private static readonly HashSet<string> ReservedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "true", "false", "null", "yes", "no", "on", "off", "y", "n", "~",
        };

        // 引用符なしの文字列の先頭に置けない文字と、どこにも含められない文字
        private const string PlainStartForbidden = "-?:,[]{}#&*!|>'\"%@`.+";
        private static readonly char[] PlainForbidden = { ',', '[', ']', '{', '}' };

        private readonly struct Line
        {
            public readonly int Indent;
            public readonly string Text;

            public Line(int indent, string text)
            {
                Indent = indent;
                Text = text;
            }
        }

        /// <summary>
        /// JSON のオブジェクトを YAML にする。オブジェクトでない・壊れているなら false。
        /// </summary>
        public static bool TryFromJson(string json, out string yaml)
        {
            yaml = null;
            if (!JsonObjectText.TryParseObject(json, out List<KeyValuePair<string, string>> members))
            {
                return false;
            }

            var builder = new StringBuilder();
            if (!WriteMapping(builder, members, 0, false))
            {
                return false;
            }

            yaml = builder.ToString();
            return true;
        }

        /// <summary>
        /// YAML を JSON のテキストにする（一番外側の形は問わない）。扱えない書き方・壊れているなら false。
        /// </summary>
        public static bool TryToJson(string yaml, out string json)
        {
            json = null;
            if (yaml == null || !TrySplitLines(yaml, out List<Line> lines) || lines.Count == 0)
            {
                return false;
            }

            var reader = new Reader(lines);
            if (!reader.TryReadNode(out string result) || !reader.AtEnd)
            {
                return false;
            }

            json = result;
            return true;
        }

        private static bool WriteMapping(StringBuilder builder, List<KeyValuePair<string, string>> members, int indent,
            bool firstInline)
        {
            // firstInline なら最初のキーは "- " の直後（字下げを書かない）
            for (int i = 0; i < members.Count; i++)
            {
                if (i > 0 || !firstInline)
                {
                    builder.Append(' ', indent);
                }

                builder.Append(FormatString(members[i].Key)).Append(':');
                if (!WriteValue(builder, members[i].Value, indent))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool WriteValue(StringBuilder builder, string value, int indent)
        {
            // キー・"-" の後に値を書く（中身のある入れ子は次の行から 1 段字下げして書く）
            string text = value.Trim();
            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                if (!JsonObjectText.TryParseObject(text, out List<KeyValuePair<string, string>> members))
                {
                    return false;
                }

                if (members.Count == 0)
                {
                    builder.Append(" {}\n");
                    return true;
                }

                builder.Append('\n');
                return WriteMapping(builder, members, indent + IndentWidth, false);
            }

            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                if (!JsonObjectText.TryParseArray(text, out List<string> items))
                {
                    return false;
                }

                if (items.Count == 0)
                {
                    builder.Append(" []\n");
                    return true;
                }

                builder.Append('\n');
                return WriteSequence(builder, items, indent + IndentWidth);
            }

            if (!TryFormatScalar(text, out string scalar))
            {
                return false;
            }

            builder.Append(' ').Append(scalar).Append('\n');
            return true;
        }

        private static bool WriteSequence(StringBuilder builder, List<string> items, int indent)
        {
            foreach (string item in items)
            {
                builder.Append(' ', indent).Append('-');

                // 中身のあるオブジェクトは、最初のキーを "- " と同じ行に書く
                string text = item.Trim();
                if (text.StartsWith("{", StringComparison.Ordinal)
                    && JsonObjectText.TryParseObject(text, out List<KeyValuePair<string, string>> members)
                    && members.Count > 0)
                {
                    builder.Append(' ');
                    if (!WriteMapping(builder, members, indent + IndentWidth, true))
                    {
                        return false;
                    }

                    continue;
                }

                if (!WriteValue(builder, text, indent))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryFormatScalar(string json, out string scalar)
        {
            // 文字列は必要なときだけ引用符で囲み、数値・true・false・null はそのまま
            if (json.StartsWith("\"", StringComparison.Ordinal))
            {
                bool ok = JsonObjectText.TryReadString(json, out string value);
                scalar = ok ? FormatString(value) : null;
                return ok;
            }

            scalar = json;
            return json == "true" || json == "false" || json == "null" || JsonNumber.IsMatch(json);
        }

        private static string FormatString(string value)
        {
            // JSON の文字列リテラルは YAML の二重引用符の文字列としてそのまま読める
            return IsPlainSafe(value) ? value : JsonObjectText.Quote(value);
        }

        private static bool IsPlainSafe(string value)
        {
            // 引用符なしで書いても同じ文字列に読み戻せるもの（数値に見えるもの・予約語・記号を含むものは囲む）
            if (value.Length == 0 || value != value.Trim() || ReservedWords.Contains(value)
                || PlainStartForbidden.IndexOf(value[0]) >= 0 || char.IsDigit(value[0])
                || value.IndexOfAny(PlainForbidden) >= 0
                || value.Contains(": ") || value.Contains(" #") || value.EndsWith(":", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char c in value)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c) && c != ' ')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TrySplitLines(string yaml, out List<Line> lines)
        {
            // コメント・空行・文書の区切りを除き、字下げと中身に分ける
            lines = new List<Line>();
            string[] raw = yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int n = 0; n < raw.Length; n++)
            {
                string text = raw[n];
                if (n == 0 && text.Length > 0 && text[0] == '\uFEFF')
                {
                    text = text.Substring(1);
                }

                text = StripComment(text).TrimEnd();
                int indent = 0;
                while (indent < text.Length && text[indent] == ' ')
                {
                    indent++;
                }

                if (indent == text.Length)
                {
                    continue;
                }

                // YAML では字下げにタブを使えない
                if (text[indent] == '\t')
                {
                    return false;
                }

                string content = text.Substring(indent);
                if (indent == 0 && (content == "---" || content == "..."))
                {
                    continue;
                }

                lines.Add(new Line(indent, content));
            }

            return true;
        }

        private static string StripComment(string text)
        {
            // 引用符の外で、行頭か空白の直後の # から後ろはコメント
            bool inDouble = false;
            bool inSingle = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inDouble)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inDouble = false;
                    }

                    continue;
                }

                if (inSingle)
                {
                    // '' は ' 1 文字（引用符は閉じない）
                    if (c == '\'')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inSingle = false;
                        }
                    }

                    continue;
                }

                bool afterSpace = i == 0 || text[i - 1] == ' ' || text[i - 1] == '\t';
                bool tokenStart = afterSpace || "[{,".IndexOf(text[i - 1]) >= 0;
                if (c == '"' && tokenStart)
                {
                    inDouble = true;
                }
                else if (c == '\'' && tokenStart)
                {
                    inSingle = true;
                }
                else if (c == '#' && afterSpace)
                {
                    return text.Substring(0, i);
                }
            }

            return text;
        }

        private static bool IsSequenceItem(string text)
        {
            return text == "-" || text.StartsWith("- ", StringComparison.Ordinal);
        }

        private static bool TrySplitKey(string text, out string key, out string rest)
        {
            // "キー: 値" のキーと値の部分に分ける（1 行の [ ] / { }・"- " で始まるものはキーの行ではない）
            key = null;
            rest = null;
            if (text.Length == 0 || text[0] == '[' || text[0] == '{' || IsSequenceItem(text))
            {
                return false;
            }

            int colon;
            if (text[0] == '"' || text[0] == '\'')
            {
                int index = 0;
                if (!TryReadQuoted(text, ref index, out key))
                {
                    return false;
                }

                while (index < text.Length && text[index] == ' ')
                {
                    index++;
                }

                if (index >= text.Length || text[index] != ':' || !EndsToken(text, index + 1))
                {
                    return false;
                }

                colon = index;
            }
            else
            {
                colon = -1;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == ':' && EndsToken(text, i + 1))
                    {
                        colon = i;
                        break;
                    }
                }

                if (colon <= 0)
                {
                    return false;
                }

                key = text.Substring(0, colon).TrimEnd();
            }

            rest = text.Substring(colon + 1).Trim();
            return true;
        }

        private static bool EndsToken(string text, int index)
        {
            // ":" の後が行末・空白なら区切り
            return index >= text.Length || text[index] == ' ' || text[index] == '\t';
        }

        private static bool TryReadInline(string text, out string json)
        {
            // 1 行に書いた値（[ ] / { }・引用符付きの文字列・引用符なしの値）
            json = null;
            char first = text[0];
            int index = 0;
            if (first == '[' || first == '{')
            {
                return TryReadFlow(text, ref index, out json) && IsBlankFrom(text, index);
            }

            if (first == '"' || first == '\'')
            {
                if (!TryReadQuoted(text, ref index, out string value) || !IsBlankFrom(text, index))
                {
                    return false;
                }

                json = JsonObjectText.Quote(value);
                return true;
            }

            // 複数行の文字列・アンカー・タグ・予約文字は扱わない
            if ("|>&*!%@`".IndexOf(first) >= 0)
            {
                return false;
            }

            json = PlainToJson(text);
            return true;
        }

        private static bool TryReadFlow(string text, ref int index, out string json)
        {
            // [a, b] / {k: v}（入れ子・引用符付きの文字列も可、末尾の , は許す）
            json = null;
            char open = text[index++];
            char close = open == '[' ? ']' : '}';
            var items = new List<string>();
            var members = new List<KeyValuePair<string, string>>();
            while (true)
            {
                SkipSpaces(text, ref index);
                if (index >= text.Length)
                {
                    return false;
                }

                if (text[index] == close)
                {
                    index++;
                    break;
                }

                if (open == '[')
                {
                    if (!TryReadFlowValue(text, ref index, false, out string item))
                    {
                        return false;
                    }

                    items.Add(item);
                }
                else
                {
                    if (!TryReadFlowKey(text, ref index, out string key))
                    {
                        return false;
                    }

                    // 値を省いたキーは null
                    SkipSpaces(text, ref index);
                    string value = "null";
                    if (index < text.Length && text[index] == ':')
                    {
                        index++;
                        SkipSpaces(text, ref index);
                        if (index < text.Length && text[index] != ',' && text[index] != '}'
                            && !TryReadFlowValue(text, ref index, true, out value))
                        {
                            return false;
                        }
                    }

                    members.Add(new KeyValuePair<string, string>(key, value));
                }

                SkipSpaces(text, ref index);
                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < text.Length && text[index] == close)
                {
                    index++;
                    break;
                }

                return false;
            }

            json = open == '[' ? "[" + string.Join(", ", items) + "]" : JsonObjectText.Build(members);
            return true;
        }

        private static bool TryReadFlowKey(string text, ref int index, out string key)
        {
            key = null;
            if (text[index] == '"' || text[index] == '\'')
            {
                return TryReadQuoted(text, ref index, out key);
            }

            // 引用符なしのキーは区切りの ":"・"," ・"}" の手前まで
            int start = index;
            while (index < text.Length && text[index] != ',' && text[index] != '}'
                   && !(text[index] == ':' && (index + 1 >= text.Length || " \t,}".IndexOf(text[index + 1]) >= 0)))
            {
                index++;
            }

            key = text.Substring(start, index - start).Trim();
            return key.Length > 0;
        }

        private static bool TryReadFlowValue(string text, ref int index, bool inMapping, out string json)
        {
            json = null;
            SkipSpaces(text, ref index);
            if (index >= text.Length)
            {
                return false;
            }

            char first = text[index];
            if (first == '[' || first == '{')
            {
                return TryReadFlow(text, ref index, out json);
            }

            if (first == '"' || first == '\'')
            {
                if (!TryReadQuoted(text, ref index, out string value))
                {
                    return false;
                }

                json = JsonObjectText.Quote(value);
                return true;
            }

            // 引用符なしの値は "," と閉じ括弧の手前まで
            int start = index;
            char close = inMapping ? '}' : ']';
            while (index < text.Length && text[index] != ',' && text[index] != close)
            {
                index++;
            }

            string plain = text.Substring(start, index - start).Trim();
            if (plain.Length == 0 || "|>&*!%@`[]{}".IndexOf(plain[0]) >= 0)
            {
                return false;
            }

            json = PlainToJson(plain);
            return true;
        }

        private static string PlainToJson(string text)
        {
            // 空・~・null は null、true / false は真偽値、数値に読めるものは数値、それ以外は文字列
            switch (text)
            {
                case "":
                case "~":
                case "null":
                case "Null":
                case "NULL":
                    return "null";
                case "true":
                case "True":
                case "TRUE":
                    return "true";
                case "false":
                case "False":
                case "FALSE":
                    return "false";
            }

            if (JsonNumber.IsMatch(text))
            {
                return text;
            }

            if (LooseNumber.IsMatch(text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                return number.ToString("R", CultureInfo.InvariantCulture);
            }

            return JsonObjectText.Quote(text);
        }

        private static bool TryReadQuoted(string text, ref int index, out string value)
        {
            // '...'（'' で ' を表す）か "..."（\ のエスケープ）を読み、閉じの引用符の次へ進める
            value = null;
            char quote = text[index++];
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++];
                if (quote == '\'')
                {
                    if (c != '\'')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (index < text.Length && text[index] == '\'')
                    {
                        builder.Append('\'');
                        index++;
                        continue;
                    }

                    value = builder.ToString();
                    return true;
                }

                if (c == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length || !TryAppendEscape(text, ref index, builder))
                {
                    return false;
                }
            }

            // 閉じの引用符が無い
            return false;
        }

        private static bool TryAppendEscape(string text, ref int index, StringBuilder builder)
        {
            char escaped = text[index++];
            switch (escaped)
            {
                case '0': builder.Append('\0'); return true;
                case 'a': builder.Append('\a'); return true;
                case 'b': builder.Append('\b'); return true;
                case 't':
                case '\t': builder.Append('\t'); return true;
                case 'n': builder.Append('\n'); return true;
                case 'v': builder.Append('\v'); return true;
                case 'f': builder.Append('\f'); return true;
                case 'r': builder.Append('\r'); return true;
                case 'e': builder.Append('\u001b'); return true;
                case ' ':
                case '"':
                case '/':
                case '\\':
                    builder.Append(escaped);
                    return true;
                case 'N': builder.Append('\u0085'); return true;
                case '_': builder.Append('\u00a0'); return true;
                case 'L': builder.Append('\u2028'); return true;
                case 'P': builder.Append('\u2029'); return true;
                case 'x': return TryAppendHex(text, ref index, 2, builder);
                case 'u': return TryAppendHex(text, ref index, 4, builder);
                case 'U': return TryAppendHex(text, ref index, 8, builder);
                default: return false;
            }
        }

        private static bool TryAppendHex(string text, ref int index, int digits, StringBuilder builder)
        {
            // 16 進の文字コード（\U は 0x10FFFF まで、サロゲートの単独は不可）
            if (index + digits > text.Length || !int.TryParse(text.Substring(index, digits), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out int code))
            {
                return false;
            }

            index += digits;
            if (digits < 8)
            {
                builder.Append((char)code);
                return true;
            }

            if (code < 0 || code > 0x10FFFF || code >= 0xD800 && code <= 0xDFFF)
            {
                return false;
            }

            builder.Append(char.ConvertFromUtf32(code));
            return true;
        }

        private static void SkipSpaces(string text, ref int index)
        {
            while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
            {
                index++;
            }
        }

        private static bool IsBlankFrom(string text, int index)
        {
            SkipSpaces(text, ref index);
            return index == text.Length;
        }

        /// <summary>
        /// 行の並びを字下げに従って読む（入れ子のマッピング・シーケンス）。
        /// </summary>
        private sealed class Reader
        {
            private readonly List<Line> _lines;
            private int _position;

            public Reader(List<Line> lines)
            {
                _lines = lines;
            }

            public bool AtEnd => _position >= _lines.Count;

            public bool TryReadNode(out string json)
            {
                // 今の行の形（"- "・キー・1 行の値）で読み分ける
                Line line = _lines[_position];
                if (IsSequenceItem(line.Text))
                {
                    return TryReadSequence(line.Indent, out json);
                }

                if (TrySplitKey(line.Text, out _, out _))
                {
                    return TryReadMapping(line.Indent, out json);
                }

                _position++;
                return TryReadInline(line.Text, out json);
            }

            private bool TryReadMapping(int indent, out string json)
            {
                json = null;
                var members = new List<KeyValuePair<string, string>>();
                while (_position < _lines.Count)
                {
                    Line line = _lines[_position];
                    if (line.Indent < indent)
                    {
                        break;
                    }

                    // 字下げのずれ・同じ段の "- "（キーの値でないシーケンス）は壊れている
                    if (line.Indent > indent || !TrySplitKey(line.Text, out string key, out string rest))
                    {
                        return false;
                    }

                    _position++;
                    string value = "null";
                    if (rest.Length > 0)
                    {
                        if (!TryReadInline(rest, out value))
                        {
                            return false;
                        }
                    }
                    else if (_position < _lines.Count && (_lines[_position].Indent > indent
                             || _lines[_position].Indent == indent && IsSequenceItem(_lines[_position].Text)))
                    {
                        // 値が次の行から（深い字下げの入れ子か、同じ段の "- " のシーケンス）
                        if (!TryReadNode(out value))
                        {
                            return false;
                        }
                    }

                    members.Add(new KeyValuePair<string, string>(key, value));
                }

                json = JsonObjectText.Build(members);
                return true;
            }

            private bool TryReadSequence(int indent, out string json)
            {
                json = null;
                var items = new List<string>();
                while (_position < _lines.Count)
                {
                    Line line = _lines[_position];
                    if (line.Indent < indent || line.Indent == indent && !IsSequenceItem(line.Text))
                    {
                        break;
                    }

                    if (line.Indent > indent)
                    {
                        return false;
                    }

                    // "-" の後ろと、その始まりの位置
                    string rest = line.Text.Substring(1).TrimStart(' ');
                    int restIndent = line.Indent + line.Text.Length - rest.Length;
                    string item = "null";
                    if (rest.Length == 0)
                    {
                        // 要素が次の行から（深い字下げのときだけ。無ければ null）
                        _position++;
                        if (_position < _lines.Count && _lines[_position].Indent > indent && !TryReadNode(out item))
                        {
                            return false;
                        }
                    }
                    else if (IsSequenceItem(rest) || TrySplitKey(rest, out _, out _))
                    {
                        // "- キー: 値" / "- - 値" は、"-" の後ろの位置から始まる入れ子として読む
                        _lines[_position] = new Line(restIndent, rest);
                        if (!TryReadNode(out item))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        _position++;
                        if (!TryReadInline(rest, out item))
                        {
                            return false;
                        }
                    }

                    items.Add(item);
                }

                json = "[" + string.Join(", ", items) + "]";
                return true;
            }
        }
    }
}
