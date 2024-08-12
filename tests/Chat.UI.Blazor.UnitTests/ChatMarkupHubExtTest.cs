using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.UI.Blazor.App.UnitTests;

public class ChatMarkupHubExtTest
{
    private static readonly FileExtensionContentTypeProvider FileExtensionContentTypeProvider = new ();

    [Fact]
    public void ShouldGetForChatListItemTextFromPlainText()
    {
        // arrange
        using var services = new ServiceCollection().AddTransient<IMarkupParser, MarkupParser>().BuildServiceProvider();
        var chatId = new ChatId(Generate.Option);
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 1, AssumeValid.Option);
        var chatEntry = new ChatEntry {
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
        var chatId = new ChatId(Generate.Option);
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 1, AssumeValid.Option);
        var chatEntry = new ChatEntry {
            Id = chatEntryId,
            Attachments = attachments.Select(Attachment)
                .ToApiArray(),
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
        var chatId = new ChatId(Generate.Option);
        var markupHub = new ChatMarkupHub(services, chatId);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 1, AssumeValid.Option);
        var chatEntry = new ChatEntry {
            Id = chatEntryId,
            Attachments = attachments.Select(Attachment)
                .ToApiArray(),
        };

        // act
        var markup = markupHub.GetMarkup(chatEntry, MarkupConsumer.ReactionNotification);
        var rawMarkup = MarkupFormatter.Default.Format(markup);

        // assert
        rawMarkup.Should().Be(expectedMarkupText);
    }

    private static TextEntryAttachment Attachment(string file)
    {
        if (!FileExtensionContentTypeProvider.TryGetContentType(file, out var contentType))
            throw StandardError.Constraint($"Failed to find content type for '{file}'.");

        return new TextEntryAttachment {
            Media = new Media.Media {
                FileName = file,
                ContentType = contentType,
            },
        };
    }
}
