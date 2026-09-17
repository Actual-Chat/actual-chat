namespace ActualChat.Core.UnitTests.Identifiers;

public sealed class CanonicalLanguageTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void GetCanonicalShouldMapVariantsToTheUILanguage()
    {
        // en-GB and en-US listeners must share one translation and one dub, so the variant folds
        // onto the language that ships the catalog.

        // act
        var british = Languages.GetCanonical(Language.Parse("en-GB"));
        var indian = Languages.GetCanonical(Languages.EnglishIN);
        var brazilian = Languages.GetCanonical(Languages.PortugueseBR);

        // assert
        british.Should().BeSameAs(Languages.English);
        indian.Should().BeSameAs(Languages.English);
        brazilian.Should().BeSameAs(Languages.Portuguese);
    }

    [Fact]
    public void GetCanonicalShouldKeepACanonicalLanguage()
    {
        // act
        var russian = Languages.GetCanonical(Language.Parse("ru"));
        var russianRu = Languages.GetCanonical(Language.Parse("ru-RU"));
        var kazakh = Languages.GetCanonical(Languages.Kazakh);

        // assert
        russian.Should().BeSameAs(Languages.Russian);
        russianRu.Should().BeSameAs(Languages.Russian);
        kazakh.Should().BeSameAs(Languages.Kazakh, "a language with no UI variant is canonical by itself");
    }
}
