using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ChatMarkupHubExtTest
{
    private static readonly FileExtensionContentTypeProvider FileExtensionContentTypeProvider = new ();

    [Fact]
    public void ShouldGetForChatListItemTextFromPlainText()
    {
        // arrange
        using var services = new ServiceCollection().AddTransient<IMarkupParser, MarkupParser>().BuildServiceProvider();
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

    [Theory]
    [InlineData(new[] { "img1.png" }, "Sent an image")]
    [InlineData(new[] { "img1.png", "img2.png" }, "Sent 2 images")]
    [InlineData(new[] { "img1.png", "text1.txt" }, "Sent an image and text1.txt")]
    [InlineData(new[] { "img1.png", "img2.png", "text1.txt" }, "Sent 2 images and text1.txt")]
    [InlineData(new[] { "img1.png", "text1.txt", "text2.txt" }, "Sent an image and 2 files")]
    [InlineData(new[] { "img1.png", "img2.png", "text1.txt", "text2.txt" }, "Sent 2 images and 2 files")]
    public void ShouldGetForChatListItemTextFromAttachments(string[] attachments, string expectedMarkupText)
    {
        // arrange
        using var services = new ServiceCollection().AddTransient<IMarkupParser, MarkupParser>().BuildServiceProvider();
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

    [Theory]
    [InlineData(new[] { "img1.png" }, "your image")]
    [InlineData(new[] { "img1.png", "img2.png" }, "your 2 images")]
    [InlineData(new[] { "img1.png", "text1.txt" }, "your image and text1.txt")]
    [InlineData(new[] { "img1.png", "img2.png", "text1.txt" }, "your 2 images and text1.txt")]
    [InlineData(new[] { "img1.png", "text1.txt", "text2.txt" }, "your image and 2 files")]
    [InlineData(new[] { "img1.png", "img2.png", "text1.txt", "text2.txt" }, "your 2 images and 2 files")]
    public void ShouldGetForReactionNotificationFromAttachments(string[] attachments, string expectedMarkupText)
    {
        // arrange
        using var services = new ServiceCollection().AddTransient<IMarkupParser, MarkupParser>().BuildServiceProvider();
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
    [InlineData(MarkupConsumer.Notification, "Sent a location")]
    [InlineData(MarkupConsumer.ChatListItemText, "Sent a location")]
    [InlineData(MarkupConsumer.QuoteView, "Sent a location")]
    [InlineData(MarkupConsumer.ReactionNotification, "your location")]
    public void ShouldGetLocationMarkupInsteadOfOldClientFallbackContent(
        MarkupConsumer consumer, string expectedMarkupText)
    {
        // arrange
        using var services = new ServiceCollection().AddTransient<IMarkupParser, MarkupParser>().BuildServiceProvider();
        var chatId = GroupChatId.New();
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = ChatEntryId.New(chatId, 1);
        var chatEntry = new TextEntry {
            Id = chatEntryId,
            LocationId = SharedLocationId.New(),
            Content = "\U0001F4CD Location: https://www.openstreetmap.org/?mlat=1.5&mlon=2.5\n\nUpdate Voxt to the latest version to see it on the map.",
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
        using var services = new ServiceCollection().AddTransient<IMarkupParser, MarkupParser>().BuildServiceProvider();
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
