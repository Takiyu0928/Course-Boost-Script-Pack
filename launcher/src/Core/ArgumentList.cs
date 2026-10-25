using System;
using System.Collections.Generic;
using System.Text;

namespace TimeSaver.Core
{
    /// <summary>
    /// Windows command-line argument quoting.
    ///
    /// .NET Framework 4.8 has no ProcessStartInfo.ArgumentList, so callers can only pass one
    /// Arguments string. That string is parsed by the child process with the MSVCRT rules:
    ///   - arguments are separated by spaces/tabs
    ///   - a run of backslashes that is followed by a quote has its backslashes doubled and the
    ///     quote escaped (\" )
    ///   - a run of backslashes at the end of an argument (right before the closing quote or the
    ///     end of the string) is doubled
    ///   - an empty argument must be written as ""
    ///
    /// This class implements those rules in one place so no caller ever concatenates a command
    /// line by hand, and so the behaviour is unit-testable (see tests/ArgumentQuotingTests.cs).
    /// </summary>
    public static class ArgumentList
    {
        /// <summary>True when the argument can be emitted without quotes.</summary>
        public static bool NeedsQuoting(string argument)
        {
            if (argument == null) throw new ArgumentNullException("argument");
            if (argument.Length == 0) return true;
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\v' || c == '"') return true;
            }
            return false;
        }

        /// <summary>Quote a single argument according to the MSVCRT parsing rules.</summary>
        public static string Quote(string argument)
        {
            if (argument == null) throw new ArgumentNullException("argument");
            if (!NeedsQuoting(argument)) return argument;

            StringBuilder builder = new StringBuilder(argument.Length + 8);
            builder.Append('"');
            for (int i = 0; i < argument.Length; i++)
            {
                int backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    // trailing backslashes: double them so the closing quote is not escaped
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (argument[i] == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(argument[i]);
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>Join arguments into one Arguments string. Never use for shell commands.</summary>
        public static string Join(IEnumerable<string> arguments)
        {
            if (arguments == null) return string.Empty;
            StringBuilder builder = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(Quote(argument));
            }
            return builder.ToString();
        }

        public static string Join(params string[] arguments)
        {
            return Join((IEnumerable<string>)arguments);
        }
    }
}
