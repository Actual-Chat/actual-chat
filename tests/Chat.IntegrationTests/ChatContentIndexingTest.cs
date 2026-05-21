using System.Text;
using ActualChat.Chat.Flows;
using ActualChat.Media;
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

    [Fact]
    public async Task DoesNotIndexMediaUntilUploadCompletes()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var (chatId, _) = await tester.CreateChat(true);

        // Reserve a media but don't upload it — it has no BlobId yet.
        var mediaId = await commander.Call(new Media_ReserveMedia(session, chatId.Value));
        var entry = await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Message with a not-yet-uploaded file",
            Attachments = [new ChatEntryAttachment { MediaId = mediaId }],
        });

        await Task.Delay(TimeSpan.FromSeconds(4));
        await FlowHub.NewResumeEvent<ChatMediaIndexingFlow>(chatId.Value).Schedule();

        // The flow runs but parks the entry as pending — nothing is indexed.
        await TestExt.When(async () => {
            var flow = await FlowHub.TryGet<ChatMediaIndexingFlow>(chatId.Value);
            flow.Should().NotBeNull();
            flow!.PendingEntryLids.Should().Contain(entry.LocalId);
        }, TimeSpan.FromSeconds(30));
        (await chats.ListChatContent(session, chatId, ChatContentKind.All, CancellationToken.None))
            .Should().BeEmpty();

        // Complete the upload — the media gets a BlobId.
        var data = "uploaded file content"u8.ToArray();
        var metadata = new PropertyBag()
            .Set("FileName", "doc.txt")
            .Set("ContentType", "text/plain");
        var uploadId = await commander.Call(
            new Uploads_Create(session, data.Length, $"MediaUploadTest/v1/{chatId.Value}", metadata));
        await commander.Call(new Uploads_Append(session, uploadId, 0, data));
        await commander.Call(new Media_UpdateProgress(session, mediaId, null, MediaProcessingStage.Uploading, 100));
        await commander.Call(new Media_ProcessUpload(session, mediaId, uploadId));

        // On the next run the pending entry is rechecked and indexed.
        await FlowHub.NewResumeEvent<ChatMediaIndexingFlow>(chatId.Value).Schedule();
        await TestExt.When(async () => {
            var content = await chats.ListChatContent(session, chatId, ChatContentKind.All, CancellationToken.None);
            content.Should().ContainSingle().Which.Kind.Should().Be(ChatContentKind.File);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GetChatContentTileReturnsIndexedItems()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var (chatId, _) = await tester.CreateChat(true);

        var fileId = await tester.SaveTextFile(chatId, "tile.txt", "tile content");
        var entry = await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "File for the tile",
            Attachments = [new ChatEntryAttachment { MediaId = fileId }],
        });
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Link for the tile https://example.net/p",
        });

        await TriggerIndexing(chatId);
        await TestExt.When(async () => {
            var content = await chats.ListChatContent(session, chatId, ChatContentKind.All, CancellationToken.None);
            content.Should().HaveCount(2);
        }, TimeSpan.FromSeconds(30));

        var tileRange = Constants.Chat.ServerIdTileStack.LastLayer.GetTile(entry.LocalId).Range;

        var tile = await chats.GetChatContentTile(session, chatId, ChatContentKind.All, tileRange, CancellationToken.None);
        tile.EntryLidTileRange.Should().Be(tileRange);
        tile.IsEmpty.Should().BeFalse();
        tile.Items.Should().HaveCount(2);

        var filesTile = await chats.GetChatContentTile(session, chatId, ChatContentKind.File, tileRange, CancellationToken.None);
        filesTile.Items.Should().ContainSingle().Which.Kind.Should().Be(ChatContentKind.File);
    }

    [Fact]
    public async Task MasterFlowBackfillsExistingContent()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var (chatId, _) = await tester.CreateChat(true);

        var fileId = await tester.SaveTextFile(chatId, "backfill.txt", "backfill content");
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Pre-existing file",
            Attachments = [new ChatEntryAttachment { MediaId = fileId }],
        });
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Pre-existing link https://example.com/backfill",
        });

        // Reset + resume the master flow so it re-runs Init and backfills every chat from scratch.
        await Task.Delay(TimeSpan.FromSeconds(4));
        await FlowHub.NewResumeEvent<ChatContentIndexingMasterFlow>("").WithReset(true).Schedule();

        await TestExt.When(async () => {
            var content = await chats.ListChatContent(session, chatId, ChatContentKind.All, CancellationToken.None);
            content.Should().HaveCount(2);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task NonMemberCannotReadChatContent()
    {
        await using var ownerTester = AppHost.NewBlazorTester(Out);
        await ownerTester.SignInAsUniqueBob();
        var (chatId, _) = await ownerTester.CreateChat(false); // private chat

        var ownerChats = ownerTester.AppServices.GetRequiredService<IChats>();
        var ownerContent = await ownerChats.ListChatContent(
            ownerTester.Session, chatId, ChatContentKind.All, CancellationToken.None);
        ownerContent.Should().BeEmpty();

        await using var outsiderTester = AppHost.NewBlazorTester(Out);
        await outsiderTester.SignInAsUniqueAlice();
        var outsiderChats = outsiderTester.AppServices.GetRequiredService<IChats>();
        var outsiderSession = outsiderTester.Session;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            outsiderChats.ListChatContent(outsiderSession, chatId, ChatContentKind.All, CancellationToken.None));

        var tileRange = Constants.Chat.ServerIdTileStack.LastLayer.GetTile(0).Range;
        await Assert.ThrowsAnyAsync<Exception>(() =>
            outsiderChats.GetChatContentTile(outsiderSession, chatId, ChatContentKind.All, tileRange, CancellationToken.None));
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
