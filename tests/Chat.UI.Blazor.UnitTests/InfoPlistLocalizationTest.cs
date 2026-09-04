using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

// The NS*UsageDescription prompts are rendered by the OS while the app is not running, so they
// live outside the catalog, in <lang>.lproj/InfoPlist.strings under App.Maui. This keeps every UI
// language's file complete and the three Info.plist files in step with the English one.

public class InfoPlistLocalizationTest
{
    private const string PlistKeySuffix = "UsageDescription";
    private const string LprojRoot = "src/dotnet/App.Maui/Platforms/iOS/Resources";
    private static readonly string[] InfoPlistPaths = [
        "src/dotnet/App.Maui/Platforms/iOS/Info.plist",
        "src/dotnet/App.Maui/Platforms/MacCatalyst/Info.plist",
        "src/dotnet/App.Maui/Platforms/MacOS/Info.plist",
    ];
    // Apple names the Chinese folders by script, and knows no Montenegrin at all
    private static readonly Dictionary<string, string?> LprojNames = new() {
        [Languages.Chinese.IsoCode] = "zh-Hans",
        [Languages.Montenegrin.IsoCode] = null,
    };
    private static readonly Regex EntryRe = new("^\"(?<key>[^\"]+)\" = \"(?<value>(?:[^\"\\\\]|\\\\.)*)\";$");

    [Fact]
    public void EveryUILanguageShouldShipItsInfoPlistStrings()
    {
        // arrange
        var enKeys = EnglishStrings().Keys.ToHashSet();

        // act
        var errors = new List<string>();
        foreach (var language in Languages.AllUI) {
            var lproj = LprojNames.GetValueOrDefault(language.IsoCode, language.IsoCode);
            if (lproj == null)
                continue;

            var path = LprojPath(lproj);
            if (!File.Exists(path)) {
                errors.Add($"'{language.IsoCode}' has no {lproj}.lproj/InfoPlist.strings");
                continue;
            }

            var strings = ParseStrings(path);
            foreach (var key in enKeys.Except(strings.Keys))
                errors.Add($"'{lproj}' lacks {key}");
            foreach (var key in strings.Keys.Except(enKeys))
                errors.Add($"'{lproj}' has an extra {key}");
            foreach (var (key, value) in strings)
                if (value.IsNullOrEmpty())
                    errors.Add($"'{lproj}.{key}' is empty");
        }

        // assert
        errors.Should().BeEmpty(
            "every UI language must translate the same usage descriptions as en.lproj:\n{0}",
            string.Join("\n", errors));
    }

    [Fact]
    public void EveryInfoPlistShouldCarryTheEnglishStrings()
    {
        // The raw Info.plist values are what a device with no matching .lproj falls back to,
        // so they are pinned to en.lproj rather than maintained as a second copy.

        // arrange
        var en = EnglishStrings();

        // act
        var errors = new List<string>();
        foreach (var relativePath in InfoPlistPaths) {
            var plist = XDocument.Load(Path.Combine(TestRepository.Root.FullName, relativePath));
            var entries = plist.Root!.Element("dict")!.Elements("key")
                .Select(k => (Key: k.Value, Value: k.ElementsAfterSelf().First().Value))
                .Where(x => x.Key.EndsWith(PlistKeySuffix))
                .ToList();
            entries.Should().NotBeEmpty($"{relativePath} must declare usage descriptions");
            foreach (var (key, value) in entries) {
                if (!en.TryGetValue(key, out var enValue))
                    errors.Add($"{relativePath}: {key} is not in en.lproj/InfoPlist.strings");
                else if (enValue != value)
                    errors.Add($"{relativePath}: {key} differs from en.lproj/InfoPlist.strings");
            }
        }

        // assert
        errors.Should().BeEmpty(
            "every Info.plist usage description must match en.lproj:\n{0}", string.Join("\n", errors));
    }

    // Private methods

    private static Dictionary<string, string> EnglishStrings()
    {
        var strings = ParseStrings(LprojPath("en"));
        strings.Should().NotBeEmpty("en.lproj/InfoPlist.strings must define the usage descriptions");
        return strings;
    }

    private static string LprojPath(string lproj)
        => Path.Combine(TestRepository.Root.FullName, LprojRoot, lproj + ".lproj", "InfoPlist.strings");

    private static Dictionary<string, string> ParseStrings(string path)
        => File.ReadLines(path)
            .Select(line => EntryRe.Match(line))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups["key"].Value, m => Regex.Unescape(m.Groups["value"].Value));
}
