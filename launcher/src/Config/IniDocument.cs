using System;
using System.Collections.Generic;
using System.Text;

namespace TimeSaver.Config
{
    public enum IniLineKind
    {
        Blank,
        Comment,
        Section,
        Entry,
        Other
    }

    /// <summary>One physical line of an INI file, kept in a form that can be re-rendered losslessly.</summary>
    public sealed class IniLine
    {
        public IniLineKind Kind;
        /// <summary>Original text for lines we never rewrite (comments, unknown constructs, section headers).</summary>
        public string Raw = string.Empty;
        public string Section = string.Empty;
        public string Key = string.Empty;
        /// <summary>Everything before the value, e.g. "username = " or "limitSpeed=".</summary>
        public string Prefix = string.Empty;
        /// <summary>The value itself, trimmed on the right only.</summary>
        public string Value = string.Empty;
        /// <summary>Trailing inline comment including its leading whitespace, e.g. " ; 默认:True".</summary>
        public string Suffix = string.Empty;
    }

    /// <summary>
    /// Minimal, comment-preserving INI reader/writer.
    ///
    /// Autovisor's config.ini holds user data we must not destroy: comments, unknown sections,
    /// unknown keys, blank-line rhythm and the original encoding/newline style. This type only
    /// ever rewrites the value of entries the caller explicitly sets; every other byte of the
    /// file is reproduced from Raw.
    /// </summary>
    public sealed class IniDocument
    {
        private readonly List<IniLine> _lines = new List<IniLine>();

        public IList<IniLine> Lines { get { return _lines; } }
        public bool TrailingNewLine { get; set; }

        public static IniDocument Parse(string text)
        {
            IniDocument document = new IniDocument();
            if (text == null) text = string.Empty;
            document.TrailingNewLine = text.Length > 0 && (text[text.Length - 1] == '\n' || text[text.Length - 1] == '\r');

            string[] rawLines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int count = rawLines.Length;
            if (document.TrailingNewLine && count > 0) count--; // last element is the text after the final newline

            string currentSection = string.Empty;
            for (int i = 0; i < count; i++)
            {
                string raw = rawLines[i];
                string trimmed = raw.Trim();
                IniLine line = new IniLine();
                line.Raw = raw;

                if (trimmed.Length == 0)
                {
                    line.Kind = IniLineKind.Blank;
                }
                else if (trimmed[0] == ';' || trimmed[0] == '#')
                {
                    line.Kind = IniLineKind.Comment;
                }
                else if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    line.Kind = IniLineKind.Section;
                    line.Section = currentSection;
                }
                else
                {
                    int equals = raw.IndexOf('=');
                    if (equals <= 0)
                    {
                        line.Kind = IniLineKind.Other;
                    }
                    else
                    {
                        string key = raw.Substring(0, equals).Trim();
                        if (key.Length == 0)
                        {
                            line.Kind = IniLineKind.Other;
                        }
                        else
                        {
                            int valueStart = equals + 1;
                            while (valueStart < raw.Length && (raw[valueStart] == ' ' || raw[valueStart] == '\t')) valueStart++;

                            int commentAt = FindInlineComment(raw, valueStart);
                            string valuePart;
                            string suffix;
                            if (commentAt >= 0)
                            {
                                // keep the whitespace that separated the value from its inline comment
                                int valueEnd = commentAt;
                                while (valueEnd > valueStart && (raw[valueEnd - 1] == ' ' || raw[valueEnd - 1] == '\t')) valueEnd--;
                                valuePart = raw.Substring(valueStart, valueEnd - valueStart);
                                suffix = raw.Substring(valueEnd);
                            }
                            else
                            {
                                valuePart = raw.Substring(valueStart);
                                suffix = string.Empty;
                            }

                            line.Kind = IniLineKind.Entry;
                            line.Section = currentSection;
                            line.Key = key;
                            line.Prefix = raw.Substring(0, valueStart);
                            line.Value = valuePart.TrimEnd();
                            line.Suffix = suffix;
                        }
                    }
                }

                document._lines.Add(line);
            }

            return document;
        }

        private static int FindInlineComment(string raw, int from)
        {
            bool inSingle = false;
            bool inDouble = false;
            for (int i = from; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (!inSingle && !inDouble && (c == ';' || c == '#'))
                {
                    bool precededBySpace = i == from || raw[i - 1] == ' ' || raw[i - 1] == '\t';
                    if (precededBySpace) return i;
                }
            }
            return -1;
        }

        public string Render(string newLine)
        {
            if (newLine == null) newLine = "\r\n";
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                IniLine line = _lines[i];
                if (line.Kind == IniLineKind.Entry) builder.Append(line.Prefix).Append(line.Value).Append(line.Suffix);
                else builder.Append(line.Raw);
                if (i < _lines.Count - 1 || TrailingNewLine) builder.Append(newLine);
            }
            return builder.ToString();
        }

        public bool Has(string section, string key)
        {
            return FindEntry(section, key) != null;
        }

        public string Get(string section, string key, string fallback)
        {
            IniLine line = FindEntry(section, key);
            return line == null ? fallback : line.Value;
        }

        public bool GetBool(string section, string key, bool fallback)
        {
            string value = Get(section, key, null);
            if (value == null) return fallback;
            string normalized = value.Trim();
            if (normalized.Equals("true", StringComparison.OrdinalIgnoreCase) || normalized == "1") return true;
            if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase) || normalized == "0") return false;
            return fallback;
        }

        public double GetDouble(string section, string key, double fallback)
        {
            string value = Get(section, key, null);
            if (value == null) return fallback;
            double parsed;
            if (double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed)) return parsed;
            return fallback;
        }

        /// <summary>
        /// Sets a value, reusing the existing line (so its spacing and inline comment survive).
        /// A missing key is inserted at the end of its section; a missing section is appended.
        /// </summary>
        public void Set(string section, string key, string value)
        {
            if (section == null) section = string.Empty;
            if (key == null) throw new ArgumentNullException("key");
            if (value == null) value = string.Empty;

            IniLine existing = FindEntry(section, key);
            if (existing != null)
            {
                existing.Value = value;
                return;
            }

            int insertAt = FindSectionEnd(section);
            if (insertAt < 0)
            {
                // new section goes to the end of the file
                if (_lines.Count > 0 && _lines[_lines.Count - 1].Kind != IniLineKind.Blank)
                    _lines.Add(new IniLine { Kind = IniLineKind.Blank, Raw = string.Empty });
                _lines.Add(new IniLine { Kind = IniLineKind.Section, Raw = "[" + section + "]", Section = section });
                _lines.Add(new IniLine
                {
                    Kind = IniLineKind.Entry,
                    Section = section,
                    Key = key,
                    Prefix = key + " = ",
                    Value = value,
                    Suffix = string.Empty
                });
                TrailingNewLine = true;
                return;
            }

            // reuse only the separator style ("= ", " = ", "=") of the previous entry, never its key name
            IniLine template = FindLastEntryInSection(section);
            string separator = " = ";
            if (template != null)
            {
                int equals = template.Prefix.IndexOf('=');
                if (equals >= 0)
                {
                    // keep the whitespace that sat before '=' as well (" = " / "=" / "\t= ")
                    int leftStart = equals;
                    while (leftStart > 0 && (template.Prefix[leftStart - 1] == ' ' || template.Prefix[leftStart - 1] == '\t'))
                        leftStart--;
                    separator = template.Prefix.Substring(leftStart);
                }
            }
            string prefix = key + separator;

            _lines.Insert(insertAt, new IniLine
            {
                Kind = IniLineKind.Entry,
                Section = section,
                Key = key,
                Prefix = prefix,
                Value = value,
                Suffix = string.Empty
            });
        }

        /// <summary>
        /// Removes a single entry line. Comments, blank lines and every other line stay untouched,
        /// which is what "delete a course URL" needs: the section keeps its comments/placeholder.
        /// </summary>
        public bool Remove(string section, string key)
        {
            if (section == null) section = string.Empty;
            if (key == null) throw new ArgumentNullException("key");
            for (int i = 0; i < _lines.Count; i++)
            {
                IniLine line = _lines[i];
                if (line.Kind != IniLineKind.Entry) continue;
                if (!string.Equals(line.Section, section, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                _lines.RemoveAt(i);
                return true;
            }
            return false;
        }

        private IniLine FindEntry(string section, string key)
        {
            if (section == null) section = string.Empty;
            for (int i = 0; i < _lines.Count; i++)
            {
                IniLine line = _lines[i];
                if (line.Kind != IniLineKind.Entry) continue;
                if (!string.Equals(line.Section, section, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                return line;
            }
            return null;
        }

        private IniLine FindLastEntryInSection(string section)
        {
            IniLine result = null;
            for (int i = 0; i < _lines.Count; i++)
            {
                IniLine line = _lines[i];
                if (line.Kind != IniLineKind.Entry) continue;
                if (string.Equals(line.Section, section, StringComparison.OrdinalIgnoreCase)) result = line;
            }
            return result;
        }

        private int FindSectionEnd(string section)
        {
            int sectionStart = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                IniLine line = _lines[i];
                if (line.Kind == IniLineKind.Section &&
                    string.Equals(line.Section, section, StringComparison.OrdinalIgnoreCase))
                {
                    sectionStart = i;
                    break;
                }
            }
            if (sectionStart < 0) return -1;

            int lastEntry = sectionStart;
            for (int i = sectionStart + 1; i < _lines.Count; i++)
            {
                IniLine line = _lines[i];
                if (line.Kind == IniLineKind.Section) break;
                if (line.Kind == IniLineKind.Entry) lastEntry = i;
            }
            return lastEntry + 1;
        }
    }
}
