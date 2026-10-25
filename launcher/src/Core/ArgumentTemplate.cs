using System;
using System.Collections.Generic;

namespace TimeSaver.Core
{
    /// <summary>
    /// Expands "{dir}" / "{profile}" style tokens inside an argument template.
    ///
    /// Tokens are replaced inside the element and the result is passed to ArgumentList as one
    /// argument, which then quotes it. User text never reaches a shell: there is no cmd.exe, no
    /// quoting by hand and no string concatenation of a command line.
    /// </summary>
    public static class ArgumentTemplate
    {
        public static string[] Expand(string[] template, IDictionary<string, string> values)
        {
            if (template == null) return new string[0];
            string[] result = new string[template.Length];
            for (int i = 0; i < template.Length; i++)
            {
                string current = template[i] ?? string.Empty;
                if (values != null)
                {
                    foreach (KeyValuePair<string, string> pair in values)
                    {
                        string token = "{" + pair.Key + "}";
                        if (current.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                            current = Replace(current, token, pair.Value ?? string.Empty);
                    }
                }
                result[i] = current;
            }
            return result;
        }

        private static string Replace(string text, string token, string value)
        {
            int index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                text = text.Substring(0, index) + value + text.Substring(index + token.Length);
                index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            }
            return text;
        }
    }
}
