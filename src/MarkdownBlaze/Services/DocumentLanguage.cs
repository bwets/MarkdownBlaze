using System.Text.RegularExpressions;

namespace MarkdownBlaze.Services;

/// <summary>
/// Works out what language a document is written in, as the tag the viewer puts in the document's
/// <c>lang</c> attribute. The WebView hyphenates by the rules of that language — and by English
/// rules, wrongly, for everything it is not told about.
/// <para>
/// A document can say so itself with <c>lang: fr</c> in its front matter; otherwise the language is
/// recognised from its most common little words. That is crude next to a real language model, and
/// plenty for the job: prose is a third function words, and they barely overlap between languages.
/// </para>
/// </summary>
public static partial class DocumentLanguage
{
    /// <summary>
    /// The tag for text that is clearly prose but in none of the languages below. The engine has no
    /// rules for it, so the text is left unhyphenated — better than breaking it by English rules.
    /// </summary>
    public const string Undetermined = "und";

    // Each language's commonest function words. Single letters are left out on purpose: "a", "i",
    // "o", "w" and "y" are each a word in several of these languages at once.
    private static readonly (string Tag, HashSet<string> Words)[] Languages =
    [
        ("en", Set("the and of to in is that it for with as was on are this be by not or from have an at which you but can will their has")),
        ("fr", Set("le la les des et est un une du que qui dans pour pas sur au aux avec ce cette sont par plus ne se il elle nous vous ou être comme mais leur")),
        ("de", Set("der die das und ist nicht ein eine den dem des mit sich auf für von zu im auch als wird werden oder sind bei nach wie einer dass aus durch über")),
        ("es", Set("el la los las de que en un una es por con para no se del al lo como más pero sus su este esta son entre cuando también ya muy")),
        ("nl", Set("de het een en van is dat op te in voor met niet zijn er aan ook als door om bij naar of maar dan wordt worden deze dit uit nog kan over")),
        ("pl", Set("na się nie to jest do że jak ale po co tak za od przez dla są być oraz lub tym który która które jego tylko może czy już ten")),
        ("it", Set("il lo la gli le di che un una in per non con del della dei delle al alla sono come più ma si da anche questo questa nel nella")),
        ("pt", Set("os as de que um uma em para não com do da dos das no na por mais como mas se ao são foi pelo pela também ou seu sua")),
        ("sv", Set("och att det som en är på av för med till den har inte om ett de var kan så men från eller vi när också ska sig han hon")),
        ("da", Set("og at det som en er på af for med til den har ikke om et de var kan så men fra eller vi når også skal sig han hun jeg blev")),
        ("nb", Set("og at det som en er på av for med til den har ikke om et de var kan så men fra eller vi når også skal seg han hun jeg ble")),
        ("cs", Set("se na je že to do ve pro jako ale by jsou tak po nebo který která které při od být byl jeho jsem už jen podle")),
    ];

    private static HashSet<string> Set(string words) => new(words.Split(' '), StringComparer.Ordinal);

    // Enough of a document to know its language; a long one need not be read to the end.
    private const int MaxWords = 3000;

    // Below this a document is too short to judge, and too short for hyphenation to matter.
    private const int MinWords = 20;

    [GeneratedRegex(@"^\s*(?:lang|language|locale)\s*:\s*[""']?([A-Za-z]{2,3}(?:[-_][A-Za-z0-9]{2,8})*)[""']?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FrontMatterLanguage();

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Word();

    /// <summary>
    /// The language a document declares in its YAML front matter (<c>lang</c>, <c>language</c> or
    /// <c>locale</c>), as a language tag such as <c>fr</c> or <c>pt-BR</c>; null when it declares none.
    /// </summary>
    public static string? FromFrontMatter(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return null;
        var match = FrontMatterLanguage().Match(yaml);
        return match.Success ? match.Groups[1].Value.Replace('_', '-') : null;
    }

    /// <summary>
    /// The language of a run of prose: one of the tags above, <see cref="Undetermined"/> for prose in
    /// a language that is not among them, or null when there is too little text to tell.
    /// </summary>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var hits = new int[Languages.Length];
        var total = 0;
        foreach (var match in Word().EnumerateMatches(text))
        {
            var word = text.Substring(match.Index, match.Length).ToLowerInvariant();
            total++;
            for (var i = 0; i < Languages.Length; i++)
                if (Languages[i].Words.Contains(word)) hits[i]++;
            if (total >= MaxWords) break;
        }
        if (total < MinWords) return null;

        var best = 0;
        for (var i = 1; i < hits.Length; i++)
            if (hits[i] > hits[best]) best = i;

        // In its own language about a third of running text is function words; in any other, the
        // few that happen to be spelt alike stay well under a tenth.
        return hits[best] * 10 >= total ? Languages[best].Tag : Undetermined;
    }
}
