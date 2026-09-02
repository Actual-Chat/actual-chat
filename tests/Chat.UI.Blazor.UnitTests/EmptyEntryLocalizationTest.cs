using ActualChat.Localization;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class EmptyEntryLocalizationTest
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();
    private static readonly MarkupConsumer[] Consumers = [
        MarkupConsumer.Notification,
        MarkupConsumer.ChatListItemText,
        MarkupConsumer.QuoteView,
        MarkupConsumer.ReactionNotification,
    ];

    [Fact]
    public void EveryShippedLanguageShouldRenderEveryCase()
    {
        // arrange
        var errors = new List<string>();

        // act
        foreach (var language in ShippedLanguages()) {
            var builder = NewBuilder(language);
            foreach (var (entry, consumer, isLiveLocation) in Cases()) {
                var text = builder.Build(entry, consumer, isLiveLocation).ToReadableText(consumer);
                if (text.Contains("EmptyEntry_"))
                    errors.Add($"'{language.IsoCode}' leaves a key unresolved: '{text}'");
                else if (text.IsNullOrEmpty())
                    errors.Add($"'{language.IsoCode}' renders nothing for {consumer}");
            }
        }

        // assert
        errors.Should().BeEmpty(
            "every empty entry must render text in every shipped language:\n{0}", string.Join("\n", errors));
    }

    [Fact]
    public void MessageViewShouldRenderNothing()
    {
        // The bubble renders the attachments and the map themselves - text would double them.

        // arrange
        var builder = NewBuilder(Languages.English);

        // act
        var texts = Cases()
            .Select(x => builder.Build(x.Entry, MarkupConsumer.MessageView, x.IsLiveLocation))
            .Select(m => m.ToReadableText(MarkupConsumer.MessageView));

        // assert
        texts.Should().OnlyContain(x => x.Length == 0, "the message view stands nothing in");
    }

    // Private methods

    private static IEnumerable<(ChatEntry Entry, MarkupConsumer Consumer, bool IsLiveLocation)> Cases()
        => from entry in Entries()
            from consumer in Consumers
            from isLiveLocation in new[] { false, true }
            select (entry, consumer, isLiveLocation);

    private static IEnumerable<ChatEntry> Entries()
    {
        yield return new TextEntry { LocationId = SharedLocationId.New() };
        yield return NewEntry("img1.png");
        yield return NewEntry("img1.png", "img2.png");
        yield return NewEntry("vid1.mp4");
        yield return NewEntry("vid1.mp4", "vid2.mp4");
        yield return NewEntry("text1.txt");
        yield return NewEntry("text1.txt", "text2.txt");
        yield return NewEntry("img1.png", "text1.txt");
        yield return NewEntry("img1.png", "vid1.mp4", "text1.txt");
    }

    private static ChatEntry NewEntry(params string[] files)
        => new TextEntry { Attachments = files.Select(Attachment).ToArray() };

    private static ChatEntryAttachment Attachment(string file)
    {
        if (!ContentTypeProvider.TryGetContentType(file, out var contentType))
            throw StandardError.Constraint($"Failed to find content type for '{file}'.");

        return new ChatEntryAttachment {
            Media = new Media.Media(null!) { FileName = file, ContentType = contentType },
        };
    }

    private static EmptyEntryMarkupBuilder NewBuilder(Language language)
        => new LocalizedEmptyEntryMarkupBuilder(
            new TestStringLocalizer(StringCatalogs.LoadStrings(language)!, language));

    private static IEnumerable<Language> ShippedLanguages()
        => StringCatalogs.ShippedSubtags(StringCatalogs.Kind.Strings)
            .Select(s => Languages.AllUIAndTestOnly.SingleOrDefault(l => l.IsoCode == s))
            .OfType<Language>();
}
