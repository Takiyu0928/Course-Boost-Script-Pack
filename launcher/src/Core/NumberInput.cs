using System;
using System.Globalization;

namespace TimeSaver.Core
{
    /// <summary>
    /// Number parsing that refuses non-finite values.
    ///
    /// .NET Framework's double.TryParse happily accepts "NaN", "Infinity" and "-Infinity", and
    /// every range comparison against NaN is false - so "0 <= x <= 2" silently passes NaN and a
    /// NaN ends up in config.json as a bare NaN token (invalid JSON). Every numeric input in the
    /// launcher goes through this class instead.
    /// </summary>
    public static class NumberInput
    {
        public static bool TryParseFinite(string text, out double value)
        {
            value = 0;
            if (text == null) return false;
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return false;
            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0;
                return false;
            }
            return true;
        }

        public static bool TryParseFiniteRange(string text, double min, double max, out double value)
        {
            if (!TryParseFinite(text, out value)) return false;
            if (value < min || value > max)
            {
                value = 0;
                return false;
            }
            return true;
        }

        /// <summary>int.TryParse already rejects overflow; kept here so callers have one entry point.</summary>
        public static bool TryParseInt32(string text, out int value)
        {
            value = 0;
            if (text == null) return false;
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return false;
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}