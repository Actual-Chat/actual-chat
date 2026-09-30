namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechLexiconScannerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Language[] English = [Languages.English];

    private static ApiArray<SpeechSpan> Full(string text, params Language[] languages)
    {
        var spans = ApiArray<SpeechSpan>.Empty;
        foreach (var language in languages)
            spans = spans.AddNonOverlapping(SpeechLexicon.Default.FindSpans(text, language));
        return spans;
    }

    [Fact]
    public void EveryStepOfAGrowingTextShouldMatchAFullScan()
    {
        // arrange
        var text = "So, um, I went, uh, to the store. Hmm, it was, um, fine. Uhm, right.";
        var scanner = new SpeechLexiconScanner(SpeechLexicon.Default);

        // act & assert: one character at a time, as a live transcript grows
        for (var length = 1; length <= text.Length; length++) {
            var prefix = text[..length];
            scanner.Scan(prefix, English).Should().BeEquivalentTo(
                Full(prefix, Languages.English), o => o.WithStrictOrdering(), $"at length {length}");
        }
    }

    [Fact]
    public void ARevisedTailShouldDropTheMarksItNoLongerHas()
    {
        // arrange
        var scanner = new SpeechLexiconScanner(SpeechLexicon.Default);
        scanner.Scan("So, um, I went to the um", English).Should().HaveCount(2);

        // act: the recognizer rewrites the last word and adds more
        var revised = scanner.Scan("So, um, I went to the umbrella shop, uh, yes", English);

        // assert
        revised.Select(s => s.Word).Should().Equal("um", "uh");
        revised.Should().BeEquivalentTo(Full("So, um, I went to the umbrella shop, uh, yes", Languages.English));
    }

    [Fact]
    public void SeveralLanguagesShouldBeScannedTogetherAndFollowTheirSetChanging()
    {
        // arrange
        var scanner = new SpeechLexiconScanner(SpeechLexicon.Default);
        var text = "Ну, э-э, я пошёл, um, в магазин. Хм, да.";
        var both = new[] { Languages.Russian, Languages.English };

        // act
        var first = scanner.Scan(text[..12], both);
        var whole = scanner.Scan(text, both);
        var russianOnly = scanner.Scan(text, [Languages.Russian]);

        // assert
        first.Should().BeEquivalentTo(Full(text[..12], Languages.Russian, Languages.English));
        whole.Should().BeEquivalentTo(Full(text, Languages.Russian, Languages.English));
        whole.Select(s => s.Word).Should().Contain("um");
        russianOnly.Should().BeEquivalentTo(Full(text, Languages.Russian));
    }

    [Fact]
    public void AScriptWithoutWordSpacesShouldMatchAFullScanAtEveryStep()
    {
        // arrange
        var japanese = Language.Parse("ja-JP");
        var text = "えーと、今日は、あのー、天気がいいですね。えー、そうですね。";
        var scanner = new SpeechLexiconScanner(SpeechLexicon.Default);

        // act & assert
        for (var length = 1; length <= text.Length; length++) {
            var prefix = text[..length];
            scanner.Scan(prefix, [japanese]).Should().BeEquivalentTo(
                Full(prefix, japanese), o => o.WithStrictOrdering(), $"at length {length}");
        }
    }
}
