namespace ActualChat.Core.UnitTests.Identifiers;

public sealed class UILanguageResolutionTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Language[] PreviewLanguages =
        Languages.All.Where(x => x.Support.HasFlag(LanguageSupport.Preview)).ToArray();

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("en-CA", "en-US")] // A regional variant we don't declare still owns the "en" catalog
    [InlineData("de-AT", "de-DE")]
    [InlineData("pt-BR", "pt-PT")]
    [InlineData("sr-Latn-RS", "sr-SR")]
    [InlineData("fil-PH", "fil-PH")] // Must not collapse onto Finnish's "fi"
    public void DetectUILanguageShouldMatchByIsoCode(string clientLanguage, string expected)
        => Languages.DetectUILanguage([clientLanguage]).Should().Be(Language.Parse(expected));

    [Theory]
    [InlineData("tl-PH", "fil-PH")]
    public void DetectUILanguageShouldResolveAliases(string clientLanguage, string expected)
        => Languages.DetectUILanguage([clientLanguage]).Should().Be(Language.Parse(expected));

    [Fact]
    public void DetectUILanguageShouldSkipPreviewLanguages()
    {
        // act, assert
        foreach (var language in PreviewLanguages)
            Languages.DetectUILanguage([language.Value]).Should().Be(Languages.Main, $"{language} isn't released");
    }

    [Theory]
    [InlineData("nb-NO")]
    [InlineData("nn-NO")]
    public void DetectUILanguageShouldSkipAliasesOfPreviewLanguages(string clientLanguage)
        => Languages.DetectUILanguage([clientLanguage]).Should().Be(Languages.Main, "Norwegian isn't released");

    [Fact]
    public void DetectUILanguageShouldTakeTheFirstSupportedLanguage()
        => Languages.DetectUILanguage(["ur-PK", "he-IL", "it-IT", "fr-FR"]).Should().Be(Languages.Italian);

    [Theory]
    [InlineData("ur-PK")] // Transcription-only until the app supports right-to-left layout
    [InlineData("")]
    [InlineData("max")]
    [InlineData("not-a-language")]
    public void DetectUILanguageShouldFallBackToMain(string clientLanguage)
        => Languages.DetectUILanguage([clientLanguage]).Should().Be(Languages.Main);

    [Fact]
    public void DetectUILanguageShouldFallBackToMainWhenNothingIsReported()
        => Languages.DetectUILanguage([]).Should().Be(Languages.Main);

    [Fact]
    public void ResolveUILanguageShouldPreferTheSelectedLanguage()
        => Languages.ResolveUILanguage(null, Languages.Japanese, ["de-DE"]).Should().Be(Languages.Japanese);

    [Fact]
    public void ResolveUILanguageShouldDetectWhenNothingIsSelected()
        // null is the "auto" selection rather than a missing value: it means "follow the device"
        => Languages.ResolveUILanguage(null, null, ["de-DE"]).Should().Be(Languages.German);

    [Theory]
    [InlineData("de-DE", "de-DE")]
    [InlineData("de-AT", "de-DE")]
    [InlineData("ru", "ru-RU")]
    public void ResolveUILanguageShouldPreferTheUrlOverride(string languageOverride, string expected)
        => Languages.ResolveUILanguage(languageOverride, Languages.Japanese, ["fr-FR"])
            .Should().Be(Language.Parse(expected));

    [Theory]
    [InlineData("nb-NO", "no-NO")]
    [InlineData("nn-NO", "no-NO")]
    public void ResolveUILanguageShouldResolveAliasesOfTheUrlOverride(string languageOverride, string expected)
        => Languages.ResolveUILanguage(languageOverride, null, []).Should().Be(Language.Parse(expected));

    [Fact]
    public void ResolveUILanguageShouldReachPreviewLanguagesThroughTheUrlOverride()
    {
        // act, assert
        foreach (var language in PreviewLanguages)
            Languages.ResolveUILanguage(language.Value, null, ["de-DE"])
                .Should().Be(language, $"{language} has a catalog");
    }

    [Theory]
    [InlineData("max")]
    [InlineData("MAX")]
    public void ResolveUILanguageShouldAcceptMaxOnlyAsTheUrlOverride(string languageOverride)
        => Languages.ResolveUILanguage(languageOverride, Languages.Japanese, ["fr-FR"])
            .Should().Be(Languages.Max);

    [Fact]
    public void ResolveUILanguageShouldNotReachLanguagesWithoutCatalogThroughTheUrlOverride()
        => Languages.ResolveUILanguage("ur-PK", null, ["de-DE"]).Should().Be(Languages.Main);

    [Theory]
    [InlineData("")]
    [InlineData("max-US")]
    [InlineData("not-a-language")]
    public void ResolveUILanguageShouldNotFallThroughWhenTheUrlOverrideIsInvalid(string languageOverride)
        => Languages.ResolveUILanguage(languageOverride, Languages.Japanese, ["de-DE"]).Should().Be(Languages.Main);
}
