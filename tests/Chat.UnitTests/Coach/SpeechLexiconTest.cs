using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechLexiconTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly string[] Isos = [
        "bg", "bs", "cs", "de", "en", "es", "fr", "hi", "id", "it", "ja", "ko", "pl", "pt", "ru", "tr", "uk", "vi", "zh",
    ];

    [Fact]
    public void FindSpansShouldMarkFilledPausesAndKeepTheirPositions()
    {
        // arrange
        const string text = "So, um, I went to the store, uhh, and Umbrella and humid are words.";

        // act
        var spans = SpeechLexicon.Default.FindSpans(text, Language.Parse("en-US"));

        // assert
        spans.Select(s => text.Substring(s.Start, s.Length)).Should().Equal("um", "uhh");
        spans.Should().OnlyContain(s => s.Kind == SpeechSpanKind.FilledPause);
    }

    [Theory]
    [InlineData("ru-RU", "Ну, э-э-э, я думаю, эээ, что мм да.", "э-э-э", "эээ", "мм")]
    [InlineData("de-DE", "Also, ähm, ich denke, äh, das geht.", "ähm", "äh")]
    [InlineData("fr-FR", "Alors, euh, je pense, hum, que oui.", "euh", "hum")]
    [InlineData("uk-UA", "Ну, е-е-е, я думаю, гм, що так.", "е-е-е", "гм")]
    [InlineData("ja-JP", "あの、えーと、それはですね、うーん、そうです。", "えーと", "うーん")]
    public void FindSpansShouldWorkPerLanguage(string language, string text, params string[] expected)
    {
        // act
        var spans = SpeechLexicon.Default.FindSpans(text, Language.Parse(language));

        // assert
        spans.Select(s => text.Substring(s.Start, s.Length)).Should().Equal(expected);
    }

    [Fact]
    public void FindSpansShouldFindNothingWithoutALanguageOrForAnUnknownOne()
    {
        // act & assert
        SpeechLexicon.Default.FindSpans("um, uh", null).Should().BeEmpty();
        SpeechLexicon.Default.FindSpans("um, uh", Language.Parse("sv-SE")).Should().BeEmpty();
    }

    [Fact]
    public void EveryLanguageOfTheAppShouldHaveFilledPauses()
    {
        // assert
        SpeechLexicon.Default.Languages.Should().BeEquivalentTo(Isos);
        foreach (var iso in Isos)
            SpeechLexicon.Default.Count(iso, SpeechSpanKind.FilledPause).Should().BeGreaterThan(0, iso);
    }
}
