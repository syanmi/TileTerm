using System.Collections.Generic;
using System.Text;

namespace TileTerm.Terminal;

/// <summary>
/// Converts between the <c>string[]</c> argument array used internally and
/// the single command-line-style text a settings text box edits (e.g.
/// <c>--login -i</c> or <c>-NoLogo -Command "some thing"</c>).
/// </summary>
internal static class CommandLineText
{
    public static string Join(string[] args)
    {
        var sb = new StringBuilder();
        foreach (var a in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (a.Length == 0 || a.Contains(' '))
                sb.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
            else
                sb.Append(a);
        }
        return sb.ToString();
    }

    /// <summary>Splits on whitespace, respecting double-quoted segments.</summary>
    public static string[] Split(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                continue;
            }
            current.Append(c);
            hasToken = true;
        }
        if (hasToken) result.Add(current.ToString());

        return result.ToArray();
    }
}
