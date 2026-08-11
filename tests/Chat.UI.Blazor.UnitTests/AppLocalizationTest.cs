using System.Text.RegularExpressions;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class AppLocalizationTest
{
    private const StringCatalogs.Kind Strings = StringCatalogs.Kind.Strings;
    private const StringCatalogs.Kind Messages = StringCatalogs.Kind.Messages;
    private const string PrefixSuffix = "_Prefix";
    private const string SuffixSuffix = "_Suffix";
    private static readonly Regex PlaceholderRe = new(@"\{\d+\}");

    [Theory]
    [InlineData(Strings)]
    [InlineData(Messages)]
    public void EnglishFallbackShouldBeComplete(StringCatalogs.Kind kind)
    {
        // Any language without its own translation is resolved via the English fallback,
        // so every catalog language is "supported" as long as English defines the full key set.

        // act
        var en = Load(Languages.English.IsoCode, kind);

        // assert
        en.Should().NotBeNull();
        en!.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(Strings)]
    [InlineData(Messages)]
    public void EveryShippedTranslationShouldMapToKnownLanguage(StringCatalogs.Kind kind)
    {
        // act
        var subtags = ShippedSubtags(kind).ToList();

        // assert
        foreach (var subtag in subtags)
            Languages.All.Should().Contain(
                l => l.IsoCode == subtag,
                $"resource '{StringCatalogs.ResourceName(kind, subtag)}' must map to a known language");
    }

    [Theory]
    [InlineData(Strings)]
    [InlineData(Messages)]
    public void EveryShippedTranslationShouldMatchEnglishKeys(StringCatalogs.Kind kind)
    {
        // arrange
        var en = Load(Languages.English.IsoCode, kind)!;
        var enKeys = en.Keys.ToHashSet();
        var placeholdersByKey = en
            .Select(kv => (kv.Key, Placeholders: PlaceholderRe.Matches(kv.Value).Select(m => m.Value).Distinct()))
            .SelectMany(x => x.Placeholders.Select(p => (x.Key, Placeholder: p)))
            .ToList();

        // act
        var translations = ShippedSubtags(kind)
            .Where(s => s != Languages.English.IsoCode)
            .ToDictionary(s => s, s => Load(s, kind)!);

        // assert
        foreach (var (subtag, dict) in translations) {
            dict.Keys.Should().BeEquivalentTo(enKeys, $"'{subtag}' must define exactly the English keys");
            foreach (var (key, placeholder) in placeholdersByKey)
                dict[key].Should().Contain(
                    placeholder, $"'{subtag}.{key}' must keep the {placeholder} placeholder");
        }
    }

    [Theory]
    [InlineData(Strings)]
    [InlineData(Messages)]
    public void EveryShippedTranslationShouldTranslateEveryEnglishKey(StringCatalogs.Kind kind)
    {
        // Guards against forgetting to translate a newly added English key:
        // every shipped translation must define a value for every English key.

        // arrange
        var enKeys = Load(Languages.English.IsoCode, kind)!.Keys.ToHashSet();

        // act
        var missingBySubtag = ShippedSubtags(kind)
            .Where(s => s != Languages.English.IsoCode)
            .Select(s => (Subtag: s, Missing: enKeys.Except(Load(s, kind)!.Keys).OrderBy(k => k).ToList()))
            .Where(x => x.Missing.Count > 0)
            .Select(x => $"'{x.Subtag}' is missing: {string.Join(", ", x.Missing)}")
            .ToList();

        // assert
        missingBySubtag.Should().BeEmpty(
            "every shipped translation must translate all English keys:\n{0}", string.Join("\n", missingBySubtag));
    }

    [Theory]
    [InlineData(Strings)]
    [InlineData(Messages)]
    public void EverySupportedUILanguageShouldShipTranslation(StringCatalogs.Kind kind)
    {
        // arrange
        var shipped = ShippedSubtags(kind).ToHashSet();

        // act
        var missing = LanguageUI.SupportedUILanguages
            .Where(l => !shipped.Contains(l.IsoCode))
            .Select(l => l.IsoCode)
            .ToList();

        // assert
        missing.Should().BeEmpty(
            "every supported UI language must ship a '{0}' resource",
            StringCatalogs.ResourceName(kind, "<subtag>"));
    }

    [Fact]
    public void CatalogsShouldNotShareKeys()
    {
        // AppStringLocalizer merges both catalogs into one forward lookup, so a shared key
        // would make Messages.* silently shadow Strings.* - and vice versa for the tests below.

        // arrange
        var strings = Load(Languages.English.IsoCode)!.Keys.ToHashSet();

        // act
        var shared = Load(Languages.English.IsoCode, Messages)!.Keys
            .Where(strings.Contains)
            .OrderBy(k => k)
            .ToList();

        // assert
        shared.Should().BeEmpty("a key must belong to exactly one catalog");
    }

    [Fact]
    public void AppStringsMembersMatchEnglishKeysExactly()
    {
        // AppStrings exposes one typed member per English key and vice-versa:
        // an unknown member is a typo/stale entry; a missing member is an untyped key.

        // Validation_* keys are excluded: they are resolved by key from ActualChat.Core,
        // which can't reference AppStrings — ValidationLocalizationTest covers them instead.

        // arrange
        var enKeys = Load(Languages.English.IsoCode)!.Keys
            .Where(k => !k.StartsWith(ValidationKeys.Prefix, StringComparison.Ordinal))
            .ToHashSet();

        // act
        var members = AppStringsMembers().Select(m => m.Name).ToHashSet();

        // assert
        members.Should().BeEquivalentTo(
            enKeys, "every English key must have exactly one AppStrings member and vice-versa");
    }

    [Fact]
    public void EveryAppStringsMemberReadsItsOwnKey()
    {
        // Members are actually invoked here, which is the only way to catch
        // a member bound to a wrong key - e.g. Common_Save => l["Common_Cancel"].

        // arrange
        var localizer = new TestStringLocalizer([]);

        // act
        var mismatches = new List<string>();
        foreach (var member in AppStringsMembers()) {
            member.Invoke(localizer, member.NewArgs());
            if (localizer.LastKey != member.Name)
                mismatches.Add($"'{member.Name}' reads key '{localizer.LastKey}'");
        }

        // assert
        mismatches.Should().BeEmpty(
            "every AppStrings member must read the key it's named after:\n{0}", string.Join("\n", mismatches));
    }

    [Fact]
    public void EveryAppStringsMemberResolvesInEveryLanguage()
    {
        // A missing key resolves to the key name, not to null or "" - so IsLastKeyFound is the
        // only signal for "absent", and an empty value can only come from the catalog itself.

        // arrange
        var members = AppStringsMembers().ToList();

        // act
        var errors = new List<string>();
        foreach (var subtag in ShippedSubtags()) {
            var localizer = new TestStringLocalizer(Load(subtag)!);
            foreach (var member in members) {
                var args = member.NewArgs();
                string value;
                try {
                    value = member.Invoke(localizer, args);
                }
                catch (TargetInvocationException e) {
                    errors.Add($"'{subtag}.{member.Name}' throws: {e.InnerException!.Message}");
                    continue;
                }
                if (!localizer.IsLastKeyFound)
                    errors.Add($"'{subtag}.{member.Name}' has no value");
                else if (value.IsNullOrEmpty() && !IsSentenceFragment(member.Name))
                    errors.Add($"'{subtag}.{member.Name}' is empty");
                else if (args.FirstOrDefault(a => !value.Contains((string)a)) is string missingArg)
                    errors.Add($"'{subtag}.{member.Name}' drops argument '{missingArg}'");
            }
        }

        // assert
        errors.Should().BeEmpty(
            "every AppStrings member must resolve to a non-empty value in every shipped language:\n{0}",
            string.Join("\n", errors));
    }

    [Fact]
    public void EverySentenceFragmentPairHasContent()
    {
        // EveryAppStringsMemberResolvesInEveryLanguage lets a _Prefix/_Suffix side be empty,
        // so this is what still catches a pair whose sentence went missing entirely.

        // arrange
        var prefixKeys = Load(Languages.English.IsoCode)!.Keys
            .Where(k => k.EndsWith(PrefixSuffix))
            .ToList();

        // act
        var errors = new List<string>();
        foreach (var subtag in ShippedSubtags()) {
            var strings = Load(subtag)!;
            foreach (var prefixKey in prefixKeys) {
                var suffixKey = prefixKey[..^PrefixSuffix.Length] + SuffixSuffix;
                if (!strings.ContainsKey(suffixKey)) {
                    errors.Add($"'{subtag}.{prefixKey}' has no matching '{suffixKey}'");
                    continue;
                }
                if (strings[prefixKey].IsNullOrEmpty() && strings[suffixKey].IsNullOrEmpty())
                    errors.Add($"'{subtag}.{prefixKey}' and '{suffixKey}' are both empty");
            }
        }

        // assert
        errors.Should().BeEmpty(
            "a _Prefix/_Suffix pair must carry the sentence on at least one side:\n{0}",
            string.Join("\n", errors));
    }

    // Private methods

    // A _Prefix/_Suffix pair wraps a value rendered as a child component; whichever side
    // the placeholder starts or ends on is legitimately empty (e.g. ja "{0} さんに挨拶しましょう!").
    private static bool IsSentenceFragment(string name)
        => name.EndsWith(PrefixSuffix) || name.EndsWith(SuffixSuffix);

    private static IEnumerable<AppStringsMember> AppStringsMembers()
    {
        const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var nested in typeof(AppStrings).GetNestedTypes(bf).Where(n => n.IsSpecialName)) {
            foreach (var p in nested.GetProperties(bf))
                yield return NewMember(p.Name, "get_" + p.Name);
            foreach (var m in nested.GetMethods(bf).Where(m => !m.IsSpecialName))
                yield return NewMember(m.Name, m.Name);
        }
        yield break;

        // Extension members are declared in a compiler-generated nested type,
        // but their implementations are static methods of AppStrings itself.
        static AppStringsMember NewMember(string name, string implementationName)
            => new(name, typeof(AppStrings).GetMethod(implementationName, bf).Require(implementationName));
    }

    private static IEnumerable<string> ShippedSubtags(StringCatalogs.Kind kind = Strings)
        => StringCatalogs.ShippedSubtags(kind);

    private static Dictionary<string, string>? Load(string subtag, StringCatalogs.Kind kind = Strings)
        => StringCatalogs.Load(kind, subtag);

    // Nested types

    private sealed record AppStringsMember(string Name, MethodInfo Implementation)
    {
        public object[] NewArgs()
            => Enumerable.Range(0, Implementation.GetParameters().Length - 1)
                .Select(object (i) => $"<arg{i}>")
                .ToArray();

        public string Invoke(IStringLocalizer localizer, object[] args)
            => (string)Implementation.Invoke(null, [localizer, ..args])!;
    }
}
