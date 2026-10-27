using ActualChat.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class LanguageTitleLocalizerExtTest
{
    public static TheoryData<string> ShippedSubtags { get; } =
        new(StringCatalogs.ShippedSubtags(StringCatalogs.Kind.Strings));

    [Fact]
    public void EnglishTitleShouldMatchLanguageTitle()
    {
        // The English catalog names every language exactly as Language.Title does, and a language
        // with no Language_* key falls back to that Title - so a mismatch here is a missing key.

        // arrange
        var l = NewLocalizer(Languages.English);

        // act
        var mismatches = Languages.All
            .Select(x => (x.Title, Localized: l.GetLanguageTitle(x)))
            .Where(x => x.Localized != x.Title)
            .Select(x => $"'{x.Localized}' != '{x.Title}'")
            .ToList();

        // assert
        mismatches.Should().BeEmpty(
            "the English catalog must name each language by its Title:\n{0}", string.Join("\n", mismatches));
    }

    [Theory]
    [MemberData(nameof(ShippedSubtags))]
    public void EveryLanguageShouldNameEveryLanguageDistinctly(string subtag)
    {
        // arrange
        var l = NewLocalizer(Languages.AllUIAndTestOnly.Single(x => x.IsoCode == subtag));

        // act
        var titles = Languages.All.Select(l.GetLanguageTitle).ToList();

        // assert
        titles.Should().OnlyHaveUniqueItems(
            "a duplicated name is a translation pasted into the wrong key, and the picker would show it twice");
    }

    // Private methods

    private static TestStringLocalizer NewLocalizer(Language language)
        => new(StringCatalogs.Load(StringCatalogs.Kind.Strings, language)!, language);
}
