using MarkdownBlaze.Services;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The language a document is written in is what the WebView hyphenates by, so a wrong answer is worse
/// than none: English rules cut French and Polish words in the wrong places. The paragraphs are the
/// fixtures in <c>tests/fixtures/languages</c>, one per language, named by the tag they must give.
/// </summary>
public class DocumentLanguageTests
{
    private static readonly string[] MixedResults = ["fr", "en", DocumentLanguage.Undetermined];

    /// <summary>One ordinary paragraph of prose per language the detector knows (<c>??.txt</c>).</summary>
    public static TheoryData<string> Languages() => TestFiles.FixtureNames("languages", "??.txt");

    [Theory]
    [MemberData(nameof(Languages))]
    public void A_paragraph_is_recognised_as_the_language_it_is_written_in(string fixture)
    {
        var tag = Path.GetFileNameWithoutExtension(fixture);

        Assert.Equal(tag, DocumentLanguage.Detect(TestFiles.Fixture("languages", fixture)));
    }

    [Fact]
    public void Prose_in_a_language_that_is_not_known_is_undetermined_not_english()
    {
        var finnish = TestFiles.Fixture("languages", "unknown-fi.txt");

        Assert.Equal(DocumentLanguage.Undetermined, DocumentLanguage.Detect(finnish));
    }

    [Fact]
    public void A_french_text_quoting_english_is_still_french()
    {
        var text = TestFiles.Fixture("languages", "fr-with-english-quote.txt");

        Assert.Equal("fr", DocumentLanguage.Detect(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ten words are too few to tell what language this is.")]
    [InlineData("Un court texte en français ne suffit pas pour que la langue soit connue.")]
    public void Too_little_text_has_no_language(string? text) =>
        Assert.Null(DocumentLanguage.Detect(text));

    [Fact]
    public void Code_is_not_prose_and_does_not_decide_the_language()
    {
        // Identifiers are words too, and almost none of them is a function word of any language.
        var code = string.Concat(Enumerable.Repeat("public static void Main string args Console WriteLine Parse Tokenize Render Flush Dispose ", 20));

        Assert.Equal(DocumentLanguage.Undetermined, DocumentLanguage.Detect(code));
    }

    [Fact]
    public void A_code_heavy_document_is_judged_by_its_prose_alone()
    {
        var prose = TestFiles.Fixture("languages", "fr.txt");
        var code = string.Concat(Enumerable.Repeat("var result = renderer.Parse(document).Tokenize().Flush(output);\n", 300));

        var result = new MarkdownService().RenderText("doc.md", $"{prose}\n```csharp\n{code}```\n");

        Assert.Equal("fr", result.Language);
    }

    [Fact]
    public void A_document_of_nothing_but_code_has_no_language()
    {
        var code = string.Concat(Enumerable.Repeat("var result = renderer.Parse(document);\n", 50));

        Assert.Null(new MarkdownService().RenderText("doc.md", $"```csharp\n{code}```\n").Language);
    }

    [Fact]
    public void An_even_mix_of_two_languages_is_one_of_them_or_undetermined_never_a_third()
    {
        const string french = "Le chat est sur le toit et les enfants jouent dans la cour avec des amis qui sont venus pour la fête. ";
        const string english = "The cat is on the roof and the children are playing in the yard with friends who came for the party. ";
        var text = string.Concat(Enumerable.Repeat(french + english, 3));

        Assert.Contains(DocumentLanguage.Detect(text), MixedResults, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("lang: fr", "fr")]
    [InlineData("Lang: FR", "FR")]
    [InlineData("language: en-GB", "en-GB")]
    [InlineData("language: pt_BR", "pt-BR")]
    [InlineData("locale: 'de'", "de")]
    [InlineData("locale: \"zh-Hant-TW\"", "zh-Hant-TW")]
    [InlineData("title: x\nlang: nl\ntags: [a, b]", "nl")]
    [InlineData("lang:fr", "fr")]
    public void The_front_matter_can_declare_the_language(string yaml, string expected) =>
        Assert.Equal(expected, DocumentLanguage.FromFrontMatter(yaml));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("title: Hello")]
    [InlineData("# lang: fr")]
    [InlineData("lang: english")]
    [InlineData("lang: ")]
    [InlineData("language-name: fr")]
    public void Anything_else_declares_no_language(string? yaml) =>
        Assert.Null(DocumentLanguage.FromFrontMatter(yaml));
}
