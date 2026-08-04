using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class AppLocalizationTest
{
    private const string Prefix = "Strings.";
    private const string Suffix = ".json";
    private static readonly Assembly Assembly = typeof(Strings).Assembly;

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
        // act
        var subtags = ShippedSubtags().ToList();

        // assert
        foreach (var subtag in subtags)
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

        // act
        var translations = ShippedSubtags()
            .Where(s => s != Languages.English.PrimarySubtag)
            .ToDictionary(s => s, s => Load(s)!);

        // assert
        foreach (var (subtag, dict) in translations) {
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
            .Select(s => (Subtag: s, Missing: enKeys.Except(Load(s)!.Keys).OrderBy(k => k).ToList()))
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

    [Fact]
    public void AppStringsMembersMatchEnglishKeysExactly()
    {
        // AppStrings exposes one typed member per English key and vice-versa:
        // an unknown member is a typo/stale entry; a missing member is an untyped key.

        // arrange
        var enKeys = Load(Languages.English.PrimarySubtag)!.Keys.ToHashSet();

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
                else if (value.IsNullOrEmpty())
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

    // Private methods

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
            => new(name, typeof(AppStrings).GetMethod(implementationName, bf)!);
    }

    private static IEnumerable<string> ShippedSubtags()
        => Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix) && n.EndsWith(Suffix))
            .Select(n => n[Prefix.Length..^Suffix.Length]);

    private static Dictionary<string, string>? Load(string subtag)
    {
        using var stream = Assembly.GetManifestResourceStream($"{Prefix}{subtag}{Suffix}");
        return stream == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }

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

    private sealed class TestStringLocalizer(Dictionary<string, string> strings) : IStringLocalizer
    {
        public string LastKey { get; private set; } = "";
        public bool IsLastKeyFound { get; private set; }

        public LocalizedString this[string name] {
            get {
                var isFound = TryGet(name, out var value);
                return new LocalizedString(name, value, !isFound);
            }
        }

        public LocalizedString this[string name, params object[] arguments] {
            get {
                var isFound = TryGet(name, out var value);
                return new LocalizedString(name, isFound ? string.Format(value, arguments) : value, !isFound);
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => strings.Select(kv => new LocalizedString(kv.Key, kv.Value));

        // Private methods

        private bool TryGet(string name, out string value)
        {
            LastKey = name;
            IsLastKeyFound = strings.TryGetValue(name, out var foundValue);
            value = foundValue ?? name;
            return IsLastKeyFound;
        }
    }
}
