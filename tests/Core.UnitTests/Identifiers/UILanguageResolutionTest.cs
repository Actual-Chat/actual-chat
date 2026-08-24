namespace ActualChat.Core.UnitTests.Identifiers;

public sealed class UILanguageResolutionTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("en-CA", "en-US")] // A regional variant we don't declare still owns the "en" catalog
    [InlineData("de-AT", "de-DE")]
    [InlineData("pt-BR", "pt-PT")]
    [InlineData("sr-Latn-RS", "sr-SR")]
    public void DetectUILanguageShouldMatchByIsoCode(string clientLanguage, string expected)
        => Languages.DetectUILanguage([clientLanguage]).Should().Be(Language.Parse(expected));

    [Fact]
    public void DetectUILanguageShouldTakeTheFirstSupportedLanguage()
        => Languages.DetectUILanguage(["nb-NO", "fi-FI", "it-IT", "fr-FR"]).Should().Be(Languages.Italian);

    [Theory]
    [InlineData("fil-PH")] // Must not collapse onto Finnish's "fi"
    [InlineData("nb-NO")]
    [InlineData("")]
    [InlineData("not-a-language")]
    public void DetectUILanguageShouldFallBackToMain(string clientLanguage)
        => Languages.DetectUILanguage([clientLanguage]).Should().Be(Languages.Main);

    [Fact]
    public void DetectUILanguageShouldFallBackToMainWhenNothingIsReported()
        => Languages.DetectUILanguage([]).Should().Be(Languages.Main);

    [Fact]
    public void ResolveUILanguageShouldPreferTheSelectedLanguage()
        => Languages.ResolveUILanguage(Languages.Japanese, ["de-DE"]).Should().Be(Languages.Japanese);

    [Fact]
    public void ResolveUILanguageShouldDetectWhenNothingIsSelected()
        // null is the "auto" selection rather than a missing value: it means "follow the device"
        => Languages.ResolveUILanguage(null, ["de-DE"]).Should().Be(Languages.German);
}
