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
            var visualMedia = await ListAllVisualMedia(chats, session, chatId);
            visualMedia.Should().HaveCount(2);
            var files = await ListAllFiles(chats, session, chatId);
            files.Should().ContainSingle();
            var links = await ListAllLinks(chats, session, chatId);
            links.Should().ContainSingle();
        }, TimeSpan.FromSeconds(30));

        var visualMediaItems = await ListAllVisualMedia(chats, session, chatId);
        visualMediaItems.Should().HaveCount(2);
        visualMediaItems.Should().Contain(x => x.MediaId == photoId && x.ContentType == "image/png");
        visualMediaItems.Should().Contain(x => x.MediaId == videoId && x.ContentType == "video/mp4");

        var fileItems = await ListAllFiles(chats, session, chatId);
        var fileItem = fileItems.Should().ContainSingle().Subject;
        fileItem.MediaId.Should().Be(fileId);
        fileItem.FileName.Should().Be("notes.txt");
    }

    [Fact]
    public async Task GifGoesToVisualMedia()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var (chatId, _) = await tester.CreateChat(true);

        var gifId = await SaveMedia(tester, chatId, "anim.gif", "image/gif");
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Look at this gif",
            Attachments = [new ChatEntryAttachment { MediaId = gifId }],
        });

        await TriggerIndexing(chatId);

        await TestExt.When(async () => {
            var visualMedia = await ListAllVisualMedia(chats, session, chatId);
            visualMedia.Should().ContainSingle().Which.ContentType.Should().Be("image/gif");
            var files = await ListAllFiles(chats, session, chatId);
            files.Should().BeEmpty();
        }, TimeSpan.FromSeconds(30));
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
            var files = await ListAllFiles(chats, session, chatId);
            files.Should().ContainSingle();
            var links = await ListAllLinks(chats, session, chatId);
            links.Should().ContainSingle();
        }, TimeSpan.FromSeconds(30));

        await commander.Call(new Chats_RemoveEntry(session, chatId, fileEntry.LocalId));
        await commander.Call(new Chats_RemoveEntry(session, chatId, linkEntry.LocalId));

        await TriggerIndexing(chatId);
        await TestExt.When(async () => {
            (await ListAllFiles(chats, session, chatId)).Should().BeEmpty();
            (await ListAllLinks(chats, session, chatId)).Should().BeEmpty();
            (await chats.GetContentPeriods(session, chatId, ChatContentKind.File, null, CancellationToken.None))
                .Periods.Should().BeEmpty();
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
        (await ListAllFiles(chats, session, chatId)).Should().BeEmpty();
        (await ListAllVisualMedia(chats, session, chatId)).Should().BeEmpty();

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
            var files = await ListAllFiles(chats, session, chatId);
            files.Should().ContainSingle().Which.FileName.Should().Be("doc.txt");
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GetContentPeriodReturnsIndexedItems()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var (chatId, _) = await tester.CreateChat(true);

        var fileId = await tester.SaveTextFile(chatId, "tile.txt", "tile content");
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "File for the period",
            Attachments = [new ChatEntryAttachment { MediaId = fileId }],
        });
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) {
            Text = "Link for the period https://example.net/p",
        });

        await TriggerIndexing(chatId);
        await TestExt.When(async () => {
            (await ListAllFiles(chats, session, chatId)).Should().ContainSingle();
            (await ListAllLinks(chats, session, chatId)).Should().ContainSingle();
        }, TimeSpan.FromSeconds(30));

        var fileSkeleton = await chats.GetContentPeriods(session, chatId, ChatContentKind.File, null, CancellationToken.None);
        fileSkeleton.Periods.Should().ContainSingle().Which.PageCount.Should().Be(1);
        fileSkeleton.NextPeriodKey.Should().BeNull();

        var filePage = await chats.GetFilePeriod(
            session, chatId, fileSkeleton.Periods[0].PeriodKey, 0, CancellationToken.None);
        filePage.Should().ContainSingle().Which.FileName.Should().Be("tile.txt");

        var linkSkeleton = await chats.GetContentPeriods(session, chatId, ChatContentKind.Link, null, CancellationToken.None);
        linkSkeleton.Periods.Should().ContainSingle().Which.PageCount.Should().Be(1);
        linkSkeleton.NextPeriodKey.Should().BeNull();
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
            (await ListAllFiles(chats, session, chatId)).Should().ContainSingle();
            (await ListAllLinks(chats, session, chatId)).Should().ContainSingle();
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task NonMemberCannotReadChatContent()
    {
        await using var ownerTester = AppHost.NewBlazorTester(Out);
        await ownerTester.SignInAsUniqueBob();
        var (chatId, _) = await ownerTester.CreateChat(false); // private chat

        await using var outsiderTester = AppHost.NewBlazorTester(Out);
        await outsiderTester.SignInAsUniqueAlice();
        var outsiderChats = outsiderTester.AppServices.GetRequiredService<IChats>();
        var outsiderSession = outsiderTester.Session;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            outsiderChats.GetContentPeriods(outsiderSession, chatId, ChatContentKind.Media, null, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            outsiderChats.GetVisualMediaPeriod(
                outsiderSession, chatId, "2026-05", 0, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            outsiderChats.GetFilePeriod(
                outsiderSession, chatId, "2026-05", 0, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            outsiderChats.GetLinkPeriod(
                outsiderSession, chatId, "2026-05", 0, CancellationToken.None));
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

    private static async Task<List<VisualMediaItem>> ListAllVisualMedia(
        IChats chats, Session session, ChatId chatId)
    {
        var result = new List<VisualMediaItem>();
        var skeleton = await chats.GetContentPeriods(session, chatId, ChatContentKind.Media, null, CancellationToken.None);
        foreach (var period in skeleton.Periods)
            for (var pageIndex = 0; pageIndex < period.PageCount; pageIndex++) {
                var page = await chats.GetVisualMediaPeriod(
                    session, chatId, period.PeriodKey, pageIndex, CancellationToken.None);
                result.AddRange(page);
            }
        return result;
    }

    private static async Task<List<FileItem>> ListAllFiles(
        IChats chats, Session session, ChatId chatId)
    {
        var result = new List<FileItem>();
        var skeleton = await chats.GetContentPeriods(session, chatId, ChatContentKind.File, null, CancellationToken.None);
        foreach (var period in skeleton.Periods)
            for (var pageIndex = 0; pageIndex < period.PageCount; pageIndex++) {
                var page = await chats.GetFilePeriod(
                    session, chatId, period.PeriodKey, pageIndex, CancellationToken.None);
                result.AddRange(page);
            }
        return result;
    }

    private static async Task<List<LinkItem>> ListAllLinks(
        IChats chats, Session session, ChatId chatId)
    {
        var result = new List<LinkItem>();
        var skeleton = await chats.GetContentPeriods(session, chatId, ChatContentKind.Link, null, CancellationToken.None);
        foreach (var period in skeleton.Periods)
            for (var pageIndex = 0; pageIndex < period.PageCount; pageIndex++) {
                var page = await chats.GetLinkPeriod(
                    session, chatId, period.PeriodKey, pageIndex, CancellationToken.None);
                result.AddRange(page);
            }
        return result;
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
