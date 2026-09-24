using System.Text;

/// <summary>
/// Utility helper for converting numbers and text containing digits into Khmer numerals (០, ១, ២, ៣, ៤, ៥, ៦, ៧, ៨, ៩).
/// </summary>
public static class KhmerNumberUtils
{
    private static readonly char[] KhmerDigits = new char[]
    {
        '\u17E0', // 0: ០
        '\u17E1', // 1: ១
        '\u17E2', // 2: ២
        '\u17E3', // 3: ៣
        '\u17E4', // 4: ៤
        '\u17E5', // 5: ៥
        '\u17E6', // 6: ៦
        '\u17E7', // 7: ៧
        '\u17E8', // 8: ៨
        '\u17E9'  // 9: ៩
    };

    /// <summary>
    /// Converts an integer into its Khmer numeral representation (e.g. 10 -> "១០", 2 -> "២").
    /// </summary>
    public static string ToKhmerNumber(int number)
    {
        return ToKhmerNumber(number.ToString());
    }

    /// <summary>
    /// Converts all ASCII digits '0'-'9' in the given text into Khmer Unicode numerals (០-៩).
    /// Preserves all non-digit characters (e.g. "01:23" -> "០១:២៣", "x2" -> "x២").
    /// </summary>
    public static string ToKhmerNumber(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        StringBuilder sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c >= '0' && c <= '9')
            {
                sb.Append(KhmerDigits[c - '0']);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
