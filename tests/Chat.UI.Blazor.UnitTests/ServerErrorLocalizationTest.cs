using System.Text.RegularExpressions;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Resources;
using ActualLab.Versioning;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

// The Error_* half of Messages.en.json is a reverse index over messages the server actually throws,
// so an English value that drifts from the code stops matching and the message silently falls back
// to AI translation in every non-English UI. Each key is anchored here either to the source literal
// it was copied from, or - for messages composed at runtime - to the call that produces it.

public class ServerErrorLocalizationTest
{
    // "…" + "…" spliced back together, so a message assembled from two literals still matches
    private static readonly Regex ConcatenationRe = new("\"\\s*\\+\\s*[$@]*\"");
    private static readonly Regex PlaceholderRe = new(@"\{\w+\}");
    private static readonly (string Message, string Key)[] Composed = [
        (StandardError.NotEnoughPermissions().Message, "Error_NotEnoughPermissions"),
        (ChatPermissionsExt.NotEnoughPermissions(ChatPermissions.Write).Message, "Error_NotEnoughPermissions_Format"),
        (StandardError.NotEnoughPermissions("Make chat public").Message, "Error_CannotMakeChatPublic"),
        (StandardError.NotEnoughPermissions("Only chat Owners and Moderators can pin messages.").Message,
            "Error_OnlyOwnersOrModeratorsCanPin"),
        (new RateLimitExceededException().Message, "Error_TooManyRequests"),
        (new RateLimitExceededException(TimeSpan.FromSeconds(5)).Message, "Error_TooManyRequests_Format"),
        (new VersionMismatchException().Message, "Error_VersionMismatch"),
        (StandardError.NotFound<Chat>().Message, "Error_ChatNotFound"),
        (StandardError.NotFound<ChatEntry>().Message, "Error_ChatEntryNotFound"),
        (StandardError.NotFound<Users.Account>().Message, "Error_AccountNotFound"),
        (StandardError.NotFound<Author>().Message, "Error_AuthorNotFound"),
        (StandardError.NotFound<Users.Avatar>().Message, "Error_AvatarNotFound"),
        (StandardError.NotFound<Contacts.Contact>().Message, "Error_ContactNotFound"),
        (StandardError.NotFound<Media.Media>().Message, "Error_MediaNotFound"),
        (StandardError.NotFound<Media.Upload>().Message, "Error_UploadNotFound"),
    ];
    // The two permission messages above wrap a literal that does live in the source, so the scan
    // below still anchors them - just to the part StandardError.NotEnoughPermissions was given.
    private static readonly Dictionary<string, string> SourceLiterals = new(StringComparer.Ordinal) {
        ["Error_CannotMakeChatPublic"] = "Make chat public",
        ["Error_OnlyOwnersOrModeratorsCanPin"] = "Only chat Owners and Moderators can pin messages.",
    };

    public static TheoryData<string, string> ComposedMessages {
        get {
            var data = new TheoryData<string, string>();
            foreach (var (message, key) in Composed)
                data.Add(message, key);
            return data;
        }
    }

    [Theory, MemberData(nameof(ComposedMessages))]
    public void ComposedMessageShouldMatchItsKey(string message, string key)
    {
        // These are assembled at runtime - by StandardError or by the exception type itself - so
        // the catalog value can't be grepped for; calling the real code is the anchor instead.

        // act
        var match = MessageIndex.Default.Match(message);

        // assert
        match.Should().NotBeNull($"\"{message}\" must resolve to '{key}'");
        match!.Key.Should().Be(key);
    }

    [Fact]
    public void EveryCatalogedErrorShouldExistInSource()
    {
        // arrange
        var sources = SourceText();
        var composedKeys = Composed.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        // act
        var orphans = new List<string>();
        var scanned = 0;
        foreach (var (key, message) in EnglishErrors()) {
            var literal = SourceLiterals.GetValueOrDefault(key);
            if (literal == null && composedKeys.Contains(key))
                continue;

            scanned++;
            if (!SourceRe(literal ?? message).IsMatch(sources))
                orphans.Add($"'{key}': \"{message}\"");
        }

        // assert
        scanned.Should().BeGreaterThan(50, "the catalog must still be dominated by plain source literals");
        orphans.Should().BeEmpty(
            "every Error_* value must byte-match a message the code still throws:\n{0}", string.Join("\n", orphans));
    }

    [Fact]
    public void NoCatalogedErrorShouldNeedTheAiFallback()
    {
        // AI translation is the safety net for text nobody catalogued; a catalogued error that
        // misses in some language would still hit it, just slower and with unreviewed wording.

        // act
        var misses = new List<string>();
        foreach (var language in LanguageUI.SupportedUILanguages) {
            var l = new TestStringLocalizer(StringCatalogs.LoadMessages(language)!);
            foreach (var (key, message) in EnglishErrors())
                if (l.ForRuntimeMessage(message) == null)
                    misses.Add($"'{language.IsoCode}.{key}'");
        }

        // assert
        misses.Should().BeEmpty(
            "every Error_* message must resolve in every language:\n{0}", string.Join("\n", misses));
    }

    [Fact]
    public void TemplateArgumentShouldSurviveTranslation()
    {
        // arrange
        var l = new TestStringLocalizer(StringCatalogs.LoadMessages(Languages.Russian)!);

        // act
        var message = l.ForRuntimeMessage("You can pin up to 7 messages.");

        // assert
        message.Should().NotBeNull();
        message.Should().NotBe("You can pin up to 7 messages.");
        message!.Should().Contain("7");
    }

    // Private methods

    private static IEnumerable<(string Key, string Message)> EnglishErrors()
        => StringCatalogs.LoadMessages(Languages.English)!
            .Where(kv => kv.Key.StartsWith(MessageIndex.ErrorPrefix, StringComparison.Ordinal))
            .Select(kv => (kv.Key, kv.Value));

    private static Regex SourceRe(string message)
        => new(string.Join(@"\{[^{}]*\}", PlaceholderRe.Split(message).Select(Regex.Escape)));

    private static string SourceText()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "ActualChat.sln")))
            root = root.Parent;
        root.Should().NotBeNull("the test must run from inside the repository to scan its sources");

        var separator = Path.DirectorySeparatorChar;
        var sources = Directory
            .EnumerateFiles(Path.Combine(root!.FullName, "src", "dotnet"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Select(File.ReadAllText);
        return ConcatenationRe.Replace(string.Join("\n", sources), "");
    }
}
