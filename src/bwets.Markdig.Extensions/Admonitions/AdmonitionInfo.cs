namespace bwets.Markdig.Extensions.Admonitions;

/// <summary>Shared parsing of the admonition info string for both syntaxes.</summary>
internal static class AdmonitionInfo
{
    public static bool TryParseDocusaurus(string info, out string kind, out string? title)
    {
        kind = string.Empty;
        title = null;
        if (info.Length == 0)
        {
            return false;
        }

        var bracket = info.IndexOf('[');
        if (bracket >= 0)
        {
            kind = info.Substring(0, bracket).Trim();
            var close = info.LastIndexOf(']');
            if (close > bracket)
            {
                title = info.Substring(bracket + 1, close - bracket - 1);
            }
        }
        else
        {
            var space = IndexOfWhitespace(info);
            if (space < 0)
            {
                kind = info;
            }
            else
            {
                kind = info.Substring(0, space);
                var rest = info.Substring(space + 1).Trim();
                if (rest.Length > 0)
                {
                    title = rest;
                }
            }
        }

        return IsWord(kind);
    }

    public static bool TryParseMkDocs(string info, out string kind, out string? title)
    {
        kind = string.Empty;
        title = null;
        if (info.Length == 0)
        {
            return false;
        }

        var space = IndexOfWhitespace(info);
        string rest;
        if (space < 0)
        {
            kind = info;
            rest = string.Empty;
        }
        else
        {
            kind = info.Substring(0, space);
            rest = info.Substring(space + 1).Trim();
        }

        var quote = rest.IndexOf('"');
        if (quote >= 0)
        {
            var quote2 = rest.IndexOf('"', quote + 1);
            if (quote2 > quote)
            {
                title = rest.Substring(quote + 1, quote2 - quote - 1);
            }
        }

        return IsWord(kind);
    }

    private static int IndexOfWhitespace(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == ' ' || s[i] == '\t')
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsWord(string s)
    {
        if (s.Length == 0 || !char.IsLetter(s[0]))
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
