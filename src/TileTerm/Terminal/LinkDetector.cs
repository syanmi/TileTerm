using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TileTerm.Terminal;

/// <summary>
/// Finds web addresses in plain terminal text, the way Windows Terminal and VS Code make printed URLs
/// clickable without any help from the program. Only http and https are recognised: those are the
/// ones it is safe to hand to the shell to open.
/// </summary>
public static partial class LinkDetector
{
    // Printable ASCII up to whitespace, quotes or angle brackets. Stopping at non-ASCII keeps Japanese
    // text written straight after a URL (「https://example.com を参照」 without the space) out of it.
    [GeneratedRegex(@"https?://[\x21-\x7E-[<>""'`]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    /// <summary>The links in <paramref name="text"/>, as (index, length) into it.</summary>
    public static List<(int Start, int Length)> Find(string text)
    {
        var links = new List<(int, int)>();
        foreach (Match match in UrlPattern().Matches(text))
        {
            int length = TrimmedLength(text.AsSpan(match.Index, match.Length));
            int schemeEnd = text.IndexOf("://", match.Index, StringComparison.Ordinal) + 3;
            if (match.Index + length > schemeEnd) links.Add((match.Index, length));
        }
        return links;
    }

    /// <summary>
    /// Drops what prose puts after a URL rather than what belongs to it: closing punctuation
    /// ("see https://a.example/x.") and a closing bracket that has no opening one inside the URL
    /// ("(https://a.example/x)"), while keeping balanced ones ("https://en.wikipedia.org/wiki/A_(B)").
    /// </summary>
    private static int TrimmedLength(ReadOnlySpan<char> url)
    {
        int length = url.Length;
        while (length > 0)
        {
            char last = url[length - 1];
            if (".,;:!?".Contains(last))
            {
                length--;
                continue;
            }
            char open = last switch { ')' => '(', ']' => '[', '}' => '{', _ => '\0' };
            if (open != '\0' && Count(url[..length], last) > Count(url[..length], open))
            {
                length--;
                continue;
            }
            break;
        }
        return length;
    }

    private static int Count(ReadOnlySpan<char> text, char c)
    {
        int n = 0;
        foreach (var ch in text)
            if (ch == c) n++;
        return n;
    }
}
