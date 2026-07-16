using ActualChat.UI.Blazor.App.Resources;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class AppLocalizationTest
{
    private const string Prefix = "Strings.";
    private const string Suffix = ".json";
    private static readonly Assembly Assembly = typeof(AppStringLocalizer).Assembly;

    [Fact]
    public void EnglishFallbackIsComplete()
    {
        // Any language without its own translation is resolved via the English fallback,
        // so every catalog language is "supported" as long as English defines the full key set.

        // act
        var en = Load(Languages.English.PrimarySubtag);

        // assert
        en.Should().NotBeNull();
        en!.Should().NotBeEmpty();
    }

    [Fact]
    public void EveryShippedTranslationMapsToKnownLanguage()
    {
        // assert
        foreach (var subtag in ShippedSubtags())
            Languages.All.Should().Contain(
                l => l.PrimarySubtag == subtag,
                $"resource '{Prefix}{subtag}{Suffix}' must map to a known language");
    }

    [Fact]
    public void EveryShippedTranslationMatchesEnglishKeys()
    {
        // arrange
        var en = Load(Languages.English.PrimarySubtag)!;
        var enKeys = en.Keys.ToHashSet();
        var formatKeys = en.Where(kv => kv.Value.Contains("{0}")).Select(kv => kv.Key);

        // assert
        foreach (var subtag in ShippedSubtags().Where(s => s != Languages.English.PrimarySubtag)) {
            var dict = Load(subtag)!;
            dict.Keys.Should().BeEquivalentTo(enKeys, $"'{subtag}' must define exactly the English keys");
            foreach (var key in formatKeys)
                dict[key].Should().Contain("{0}", $"'{subtag}.{key}' must keep the {{0}} placeholder");
        }
    }

    [Fact]
    public void EveryShippedTranslationShouldTranslateEveryEnglishKey()
    {
        // Guards against forgetting to translate a newly added English key:
        // every shipped translation must define a value for every English key.

        // arrange
        var enKeys = Load(Languages.English.PrimarySubtag)!.Keys.ToHashSet();

        // act
        var missingBySubtag = ShippedSubtags()
            .Where(s => s != Languages.English.PrimarySubtag)
            .Select(s => (Subtag: s, Missing: enKeys.Except(Load(s)!.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList()))
            .Where(x => x.Missing.Count > 0)
            .Select(x => $"'{x.Subtag}' is missing: {string.Join(", ", x.Missing)}")
            .ToList();

        // assert
        missingBySubtag.Should().BeEmpty(
            "every shipped translation must translate all English keys:\n{0}", string.Join("\n", missingBySubtag));
    }

    [Fact]
    public void EverySupportedUILanguageShouldShipTranslation()
    {
        // arrange
        var shipped = ShippedSubtags().ToHashSet();

        // act
        var missing = LanguageUI.SupportedUILanguages
            .Where(l => !shipped.Contains(l.PrimarySubtag))
            .Select(l => l.PrimarySubtag)
            .ToList();

        // assert
        missing.Should().BeEmpty("every supported UI language must ship a '{0}<subtag>{1}' resource", Prefix, Suffix);
    }

    // Private methods

    private static IEnumerable<string> ShippedSubtags()
        => Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix) && n.EndsWith(Suffix))
            .Select(n => n[Prefix.Length..^Suffix.Length]);

    private static Dictionary<string, string>? Load(string subtag)
    {
        using var stream = Assembly.GetManifestResourceStream($"{Prefix}{subtag}{Suffix}");
        return stream == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }
}
