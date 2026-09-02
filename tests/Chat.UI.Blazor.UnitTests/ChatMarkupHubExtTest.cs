using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ChatMarkupHubExtTest
{
    private static readonly FileExtensionContentTypeProvider FileExtensionContentTypeProvider = new ();
    private static readonly EmptyEntryMarkupBuilder EnglishEmptyEntryMarkupBuilder
        = new LocalizedEmptyEntryMarkupBuilder(new TestStringLocalizer(StringCatalogs.LoadStrings(Languages.English)!));

    [Fact]
    public void ShouldGetForChatListItemTextFromPlainText()
    {
        // arrange
        using var services = new ServiceCollection()
            .AddTransient<IMarkupParser, MarkupParser>()
            .AddSingleton(EnglishEmptyEntryMarkupBuilder)
            .BuildServiceProvider();
        var chatId = GroupChatId.New();
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = ChatEntryId.New(chatId, 1);
        var chatEntry = new TextEntry {
            Id = chatEntryId,
            Content = "some text",
        };

        // act
        var markup = markupHub.GetMarkup(chatEntry, MarkupConsumer.ChatListItemText);
        var rawMarkup = MarkupFormatter.Default.Format(markup);

        // assert
        rawMarkup.Should().Be("some text");
    }

    // A mixed set is named by its total, not clause by clause - see EmptyEntryMarkupBuilder.
    [Theory]
    [InlineData(new[] { "img1.png" }, "Sent 1 image")]
    [InlineData(new[] { "img1.png", "img2.png" }, "Sent 2 images")]
    [InlineData(new[] { "vid1.mp4" }, "Sent 1 video")]
    [InlineData(new[] { "text1.txt" }, "Sent text1.txt")]
    [InlineData(new[] { "text1.txt", "text2.txt" }, "Sent 2 files")]
    [InlineData(new[] { "img1.png", "text1.txt" }, "Sent 2 attachments")]
    [InlineData(new[] { "img1.png", "img2.png", "text1.txt" }, "Sent 3 attachments")]
    [InlineData(new[] { "img1.png", "text1.txt", "text2.txt" }, "Sent 3 attachments")]
    [InlineData(new[] { "img1.png", "img2.png", "text1.txt", "text2.txt" }, "Sent 4 attachments")]
    public void ShouldGetForChatListItemTextFromAttachments(string[] attachments, string expectedMarkupText)
    {
        // arrange
        using var services = new ServiceCollection()
            .AddTransient<IMarkupParser, MarkupParser>()
            .AddSingleton(EnglishEmptyEntryMarkupBuilder)
            .BuildServiceProvider();
        var chatId = GroupChatId.New();
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = ChatEntryId.New(chatId, 1);
        var chatEntry = new TextEntry {
            Id = chatEntryId,
            Attachments = attachments.Select(Attachment).ToArray(),
        };

        // act
        var markup = markupHub.GetMarkup(chatEntry, MarkupConsumer.ChatListItemText);
        var rawMarkup = MarkupFormatter.Default.Format(markup);

        // assert
        rawMarkup.Should().Be(expectedMarkupText);
    }

    // The reaction line names the target rather than counting it: "❤️ to your images".
    [Theory]
    [InlineData(new[] { "img1.png" }, "your image")]
    [InlineData(new[] { "img1.png", "img2.png" }, "your images")]
    [InlineData(new[] { "vid1.mp4" }, "your video")]
    [InlineData(new[] { "text1.txt" }, "your text1.txt")]
    [InlineData(new[] { "text1.txt", "text2.txt" }, "your files")]
    [InlineData(new[] { "img1.png", "text1.txt" }, "your attachments")]
    [InlineData(new[] { "img1.png", "img2.png", "text1.txt", "text2.txt" }, "your attachments")]
    public void ShouldGetForReactionNotificationFromAttachments(string[] attachments, string expectedMarkupText)
    {
        // arrange
        using var services = new ServiceCollection()
            .AddTransient<IMarkupParser, MarkupParser>()
            .AddSingleton(EnglishEmptyEntryMarkupBuilder)
            .BuildServiceProvider();
        var chatId = GroupChatId.New();
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = ChatEntryId.New(chatId, 1);
        var chatEntry = new TextEntry {
            Id = chatEntryId,
            Attachments = attachments.Select(Attachment).ToArray(),
        };

        // act
        var markup = markupHub.GetMarkup(chatEntry, MarkupConsumer.ReactionNotification);
        var rawMarkup = MarkupFormatter.Default.Format(markup);

        // assert
        rawMarkup.Should().Be(expectedMarkupText);
    }

    [Theory]
    [InlineData(MarkupConsumer.Notification, "\U0001F4CD Sent a location")]
    [InlineData(MarkupConsumer.ChatListItemText, "Sent a location")]
    [InlineData(MarkupConsumer.QuoteView, "\U0001F4CD Sent a location")]
    [InlineData(MarkupConsumer.ReactionNotification, "your location")]
    public void ShouldGetLocationMarkupInsteadOfOldClientFallbackContent(
        MarkupConsumer consumer, string expectedMarkupText)
    {
        // arrange
        using var services = new ServiceCollection()
            .AddTransient<IMarkupParser, MarkupParser>()
            .AddSingleton(EnglishEmptyEntryMarkupBuilder)
            .BuildServiceProvider();
        var chatId = GroupChatId.New();
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = ChatEntryId.New(chatId, 1);
        var chatEntry = new TextEntry {
            Id = chatEntryId,
            LocationId = SharedLocationId.New(),
            Content = "\U0001F4CD Location: https://www.openstreetmap.org/?mlat=1.5&mlon=2.5\n\n"
                + "Update Voxt to the latest version to see it on the map.",
        };

        // act
        var markup = markupHub.GetMarkup(chatEntry, consumer);
        var rawMarkup = MarkupFormatter.Default.Format(markup);

        // assert
        rawMarkup.Should().Be(expectedMarkupText);
    }

    [Fact]
    public void ShouldGetNotifyMembersMarkupWithoutTargetAuthorId()
    {
        // arrange
        var localizer = new TestStringLocalizer(StringCatalogs.LoadStrings(Languages.English)!);
        using var services = new ServiceCollection()
            .AddTransient<IMarkupParser, MarkupParser>()
            .AddSingleton<SystemEntryMarkupBuilder>(new LocalizedSystemEntryMarkupBuilder(localizer))
            .AddSingleton(EnglishEmptyEntryMarkupBuilder)
            .BuildServiceProvider();
        var chatId = GroupChatId.New();
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = ChatEntryId.New(chatId, 1);
        var chatEntry = new NotifyMembersEntry(chatEntryId, 1) {
            TargetAuthorName = "Alice",
        };

        // act
        var markup = markupHub.GetMarkup(chatEntry, MarkupConsumer.ChatListItemText);
        var rawMarkup = MarkupFormatter.Default.Format(markup);

        // assert
        rawMarkup.Should().Be("Alice asked for attention.");
    }

    // A live share and a one-shot pin are the same entry - only the SharedLocation behind it
    // differs - so the caller passes the fact in rather than the builder deriving it.
    [Theory]
    [InlineData(MarkupConsumer.Notification, false, "\U0001F4CD Sent a location")]
    [InlineData(MarkupConsumer.Notification, true, "\U0001F4CD Shared live location")]
    // The chat list row draws its own map-point icon, so its text carries no pin.
    [InlineData(MarkupConsumer.ChatListItemText, true, "Shared live location")]
    [InlineData(MarkupConsumer.QuoteView, false, "\U0001F4CD Sent a location")]
    // The reaction line names a target, so it stays "your location" either way.
    [InlineData(MarkupConsumer.ReactionNotification, true, "your location")]
    [InlineData(MarkupConsumer.ReactionNotification, false, "your location")]
    public void ShouldWordALiveLocationApartFromAPin(
        MarkupConsumer consumer,
        bool isLiveLocation,
        string expectedMarkupText)
    {
        // arrange
        var chatEntry = new TextEntry {
            Id = ChatEntryId.New(GroupChatId.New(), 1),
            LocationId = SharedLocationId.New(),
        };

        // act
        var markup = EnglishEmptyEntryMarkupBuilder.Build(chatEntry, consumer, isLiveLocation);

        // assert
        MarkupFormatter.Default.Format(markup).Should().Be(expectedMarkupText);
    }

    private static ChatEntryAttachment Attachment(string file)
    {
        if (!FileExtensionContentTypeProvider.TryGetContentType(file, out var contentType))
            throw StandardError.Constraint($"Failed to find content type for '{file}'.");

        return new ChatEntryAttachment {
            Media = new Media.Media(null!) {
                FileName = file,
                ContentType = contentType,
            },
        };
    }
}
