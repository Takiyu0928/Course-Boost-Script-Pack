using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TimeSaver.Config
{
    public enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        Bool,
        Null
    }

    /// <summary>A JSON node that keeps object key order and the original spelling of numbers.</summary>
    public sealed class JsonValue
    {
        public JsonKind Kind;
        public List<KeyValuePair<string, JsonValue>> Members = new List<KeyValuePair<string, JsonValue>>();
        public List<JsonValue> Items = new List<JsonValue>();
        public string Text = string.Empty;
        public bool Bool;

        public static JsonValue NewObject() { return new JsonValue { Kind = JsonKind.Object }; }
        public static JsonValue NewArray() { return new JsonValue { Kind = JsonKind.Array }; }
        public static JsonValue NewString(string value) { return new JsonValue { Kind = JsonKind.String, Text = value ?? string.Empty }; }
        public static JsonValue NewNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException("value", "JSON 数字不能是 NaN / Infinity（会写出非法 JSON）");
            return new JsonValue
            {
                Kind = JsonKind.Number,
                Text = value.ToString("R", CultureInfo.InvariantCulture)
            };
        }

        private static readonly System.Text.RegularExpressions.Regex JsonNumberPattern =
            new System.Text.RegularExpressions.Regex(@"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static JsonValue NewNumberRaw(string raw)
        {
            if (raw == null || !JsonNumberPattern.IsMatch(raw))
                throw new ArgumentException("不是合法的 JSON 数字: " + (raw ?? "(null)"), "raw");
            return new JsonValue { Kind = JsonKind.Number, Text = raw };
        }
        public static JsonValue NewBool(bool value) { return new JsonValue { Kind = JsonKind.Bool, Bool = value }; }
        public static JsonValue NewNull() { return new JsonValue { Kind = JsonKind.Null }; }

        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object || key == null) return null;
            for (int i = 0; i < Members.Count; i++)
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal)) return Members[i].Value;
            for (int i = 0; i < Members.Count; i++)
                if (string.Equals(Members[i].Key, key, StringComparison.OrdinalIgnoreCase)) return Members[i].Value;
            return null;
        }

        public bool Has(string key) { return Get(key) != null; }

        /// <summary>Replaces the value in place (keeping key order) or appends the key.</summary>
        public void Set(string key, JsonValue value)
        {
            if (Kind != JsonKind.Object) throw new InvalidOperationException("not an object");
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                {
                    Members[i] = new KeyValuePair<string, JsonValue>(Members[i].Key, value);
                    return;
                }
            }
            Members.Add(new KeyValuePair<string, JsonValue>(key, value));
        }

        public bool Remove(string key)
        {
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                {
                    Members.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public string AsString(string fallback)
        {
            if (Kind == JsonKind.String) return Text;
            if (Kind == JsonKind.Number) return Text;
            if (Kind == JsonKind.Bool) return Bool ? "true" : "false";
            return fallback;
        }

        public bool TryGetDouble(out double value)
        {
            value = 0;
            if (Kind == JsonKind.Number)
                return double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            if (Kind == JsonKind.String)
                return double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return false;
        }

        public bool TryGetInt(out int value)
        {
            value = 0;
            double parsed;
            if (!TryGetDouble(out parsed)) return false;
            if (parsed < int.MinValue || parsed > int.MaxValue) return false;
            value = (int)Math.Round(parsed);
            return true;
        }

        public bool TryGetBool(out bool value)
        {
            value = false;
            if (Kind == JsonKind.Bool) { value = Bool; return true; }
            if (Kind == JsonKind.String)
            {
                if (Text.Equals("true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
                if (Text.Equals("false", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
            }
            return false;
        }
    }

    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Small JSON reader/writer used for UnipusAI's config.json.
    ///
    /// Requirements it satisfies: keep unknown keys and their values, keep object key order,
    /// keep numbers exactly as written, keep 2-space indentation, keep the file's trailing
    /// newline. Escaped characters are decoded and re-emitted as literal characters (same
    /// semantics, different bytes) - documented in IMPLEMENTATION-REPORT.md.
    /// </summary>
    public sealed class JsonDocument
    {
        public JsonValue Root { get; set; }
        public bool TrailingNewLine { get; set; }

        public static JsonDocument Parse(string text)
        {
            if (text == null) text = string.Empty;
            Parser parser = new Parser(text);
            JsonDocument document = new JsonDocument();
            document.TrailingNewLine = text.EndsWith("\n", StringComparison.Ordinal);
            parser.SkipWhitespace();
            document.Root = parser.ReadValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new JsonParseException("JSON 末尾存在多余内容，偏移 " + parser.Position);
            return document;
        }

        public string Render()
        {
            StringBuilder builder = new StringBuilder();
            Write(builder, Root, 0);
            if (TrailingNewLine) builder.Append("\r\n");
            return builder.ToString();
        }

        private static void Write(StringBuilder builder, JsonValue value, int depth)
        {
            if (value == null) { builder.Append("null"); return; }
            switch (value.Kind)
            {
                case JsonKind.Object:
                    if (value.Members.Count == 0) { builder.Append("{}"); return; }
                    builder.Append('{');
                    for (int i = 0; i < value.Members.Count; i++)
                    {
                        if (i > 0) builder.Append(',');
                        builder.Append("\r\n");
                        Indent(builder, depth + 1);
                        WriteString(builder, value.Members[i].Key);
                        builder.Append(": ");
                        Write(builder, value.Members[i].Value, depth + 1);
                    }
                    builder.Append("\r\n");
                    Indent(builder, depth);
                    builder.Append('}');
                    return;

                case JsonKind.Array:
                    if (value.Items.Count == 0) { builder.Append("[]"); return; }
                    builder.Append('[');
                    for (int i = 0; i < value.Items.Count; i++)
                    {
                        if (i > 0) builder.Append(',');
                        builder.Append("\r\n");
                        Indent(builder, depth + 1);
                        Write(builder, value.Items[i], depth + 1);
                    }
                    builder.Append("\r\n");
                    Indent(builder, depth);
                    builder.Append(']');
                    return;

                case JsonKind.String:
                    WriteString(builder, value.Text);
                    return;

                case JsonKind.Number:
                    builder.Append(string.IsNullOrEmpty(value.Text) ? "0" : value.Text);
                    return;

                case JsonKind.Bool:
                    builder.Append(value.Bool ? "true" : "false");
                    return;

                default:
                    builder.Append("null");
                    return;
            }
        }

        private static void Indent(StringBuilder builder, int depth)
        {
            for (int i = 0; i < depth; i++) builder.Append("  ");
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text) { _text = text; _index = 0; }

            public int Position { get { return _index; } }
            public bool AtEnd { get { return _index >= _text.Length; } }

            public void SkipWhitespace()
            {
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _index++;
                    else break;
                }
            }

            public JsonValue ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new JsonParseException("JSON 意外结束");
                char c = _text[_index];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return JsonValue.NewString(ReadString());
                if (c == 't' || c == 'f') return ReadBool();
                if (c == 'n') return ReadNull();
                return ReadNumber();
            }

            private JsonValue ReadObject()
            {
                JsonValue value = JsonValue.NewObject();
                _index++; // {
                SkipWhitespace();
                if (!AtEnd && _text[_index] == '}') { _index++; return value; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != '"') throw new JsonParseException("JSON 对象缺少键名，偏移 " + _index);
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != ':') throw new JsonParseException("JSON 对象缺少冒号，偏移 " + _index);
                    _index++;
                    JsonValue member = ReadValue();
                    value.Set(key, member);
                    SkipWhitespace();
                    if (AtEnd) throw new JsonParseException("JSON 对象未闭合");
                    if (_text[_index] == ',') { _index++; continue; }
                    if (_text[_index] == '}') { _index++; return value; }
                    throw new JsonParseException("JSON 对象缺少逗号或右花括号，偏移 " + _index);
                }
            }

            private JsonValue ReadArray()
            {
                JsonValue value = JsonValue.NewArray();
                _index++; // [
                SkipWhitespace();
                if (!AtEnd && _text[_index] == ']') { _index++; return value; }
                while (true)
                {
                    value.Items.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw new JsonParseException("JSON 数组未闭合");
                    if (_text[_index] == ',') { _index++; continue; }
                    if (_text[_index] == ']') { _index++; return value; }
                    throw new JsonParseException("JSON 数组缺少逗号或右方括号，偏移 " + _index);
                }
            }

            private string ReadString()
            {
                StringBuilder builder = new StringBuilder();
                _index++; // opening quote
                while (true)
                {
                    if (AtEnd) throw new JsonParseException("JSON 字符串未闭合");
                    char c = _text[_index++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\') { builder.Append(c); continue; }
                    if (AtEnd) throw new JsonParseException("JSON 转义不完整");
                    char escape = _text[_index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length) throw new JsonParseException("JSON \\u 转义不完整");
                            string hex = _text.Substring(_index, 4);
                            int code;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw new JsonParseException("JSON \\u 转义非法: " + hex);
                            builder.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw new JsonParseException("JSON 未知转义 \\" + escape);
                    }
                }
            }

            private JsonValue ReadBool()
            {
                if (_text.Length - _index >= 4 && string.CompareOrdinal(_text, _index, "true", 0, 4) == 0)
                {
                    _index += 4;
                    return JsonValue.NewBool(true);
                }
                if (_text.Length - _index >= 5 && string.CompareOrdinal(_text, _index, "false", 0, 5) == 0)
                {
                    _index += 5;
                    return JsonValue.NewBool(false);
                }
                throw new JsonParseException("JSON 布尔值非法，偏移 " + _index);
            }

            private JsonValue ReadNull()
            {
                if (_text.Length - _index >= 4 && string.CompareOrdinal(_text, _index, "null", 0, 4) == 0)
                {
                    _index += 4;
                    return JsonValue.NewNull();
                }
                throw new JsonParseException("JSON null 非法，偏移 " + _index);
            }

            private JsonValue ReadNumber()
            {
                int start = _index;
                if (!AtEnd && (_text[_index] == '-' || _text[_index] == '+')) _index++;
                while (!AtEnd)
                {
                    char c = _text[_index];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') _index++;
                    else break;
                }
                if (_index == start) throw new JsonParseException("JSON 数值非法，偏移 " + start);
                return JsonValue.NewNumberRaw(_text.Substring(start, _index - start));
            }
        }
    }
}
