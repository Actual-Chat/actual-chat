using System.Text;
using ActualChat.Chat.Flows;
using ActualChat.Testing.Host;
using ActualChat.Uploads;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatContentIndexingTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task IndexesPhotoVideoFileAndLink()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var (chatId, _) = await tester.CreateChat(true);

        var photoId = await SaveMedia(tester, chatId, "photo.png", "image/png");
        var videoId = await SaveMedia(tester, chatId, "clip.mp4", "video/mp4");
        var fileId = await tester.SaveTextFile(chatId, "notes.txt", "file content");

        await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Message with attachments",
            Attachments = [
                new ChatEntryAttachment { MediaId = photoId },
                new ChatEntryAttachment { MediaId = videoId },
                new ChatEntryAttachment { MediaId = fileId },
            ],
        });
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Check this out https://example.com/article",
        });

        await TriggerIndexing(chatId);

        await TestExt.When(async () => {
            var content = await chats.ListChatContent(session, chatId, ChatContentKind.All, CancellationToken.None);
            content.Should().HaveCount(4);
        }, TimeSpan.FromSeconds(30));

        var items = await chats.ListChatContent(session, chatId, ChatContentKind.All, CancellationToken.None);
        items.Count(x => x.Kind == ChatContentKind.Photo).Should().Be(1);
        items.Count(x => x.Kind == ChatContentKind.Video).Should().Be(1);
        items.Count(x => x.Kind == ChatContentKind.File).Should().Be(1);
        items.Count(x => x.Kind == ChatContentKind.Link).Should().Be(1);

        var photo = items.Single(x => x.Kind == ChatContentKind.Photo);
        photo.MediaId.Should().Be(photoId);
        photo.ContentType.Should().Be("image/png");

        var file = items.Single(x => x.Kind == ChatContentKind.File);
        file.MediaId.Should().Be(fileId);
        file.FileName.Should().Be("notes.txt");

        // kindMask filtering
        var mediaOnly = await chats.ListChatContent(session, chatId, ChatContentKind.Media, CancellationToken.None);
        mediaOnly.Should().HaveCount(2);
        mediaOnly.Should().OnlyContain(x => x.Kind == ChatContentKind.Photo || x.Kind == ChatContentKind.Video);

        var linksOnly = await chats.ListChatContent(session, chatId, ChatContentKind.Link, CancellationToken.None);
        linksOnly.Should().ContainSingle().Which.Kind.Should().Be(ChatContentKind.Link);
    }

    [Fact]
    public async Task PurgesContentWhenMessagesRemoved()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var (chatId, _) = await tester.CreateChat(true);

        var fileId = await tester.SaveTextFile(chatId, "report.txt", "report content");
        var fileEntry = await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Message with a file",
            Attachments = [new ChatEntryAttachment { MediaId = fileId }],
        });
        var linkEntry = await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "A link https://example.org/page",
        });

        await TriggerIndexing(chatId);
        await TestExt.When(async () => {
            var content = await chats.ListChatContent(session, chatId, ChatContentKind.All, CancellationToken.None);
            content.Should().HaveCount(2);
        }, TimeSpan.FromSeconds(30));

        await commander.Call(new Chats_RemoveEntry(session, chatId, fileEntry.LocalId));
        await commander.Call(new Chats_RemoveEntry(session, chatId, linkEntry.LocalId));

        await TriggerIndexing(chatId);
        await TestExt.When(async () => {
            var content = await chats.ListChatContent(session, chatId, ChatContentKind.All, CancellationToken.None);
            content.Should().BeEmpty();
        }, TimeSpan.FromSeconds(30));
    }

    // Private methods

    // The indexing flows skip entries newer than ResumedAt - 2s to avoid racing with in-flight
    // writes, so the just-posted entries must age past that margin before the flows can pick them up.
    private async Task TriggerIndexing(ChatId chatId)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        await FlowHub.NewResumeEvent<ChatMediaIndexingFlow>(chatId.Value).Schedule();
        await FlowHub.NewResumeEvent<ChatEntryContentIndexingFlow>(chatId.Value).Schedule();
    }

    private static Task<MediaId> SaveMedia(IWebTester tester, ChatId chatId, string fileName, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes($"fake content for {fileName}");
        var file = new UploadedStreamFile(
            fileName,
            contentType,
            bytes.Length,
            () => Task.FromResult<Stream>(new MemoryStream(bytes)));
        return tester.SaveMedia(chatId, file);
    }
}
