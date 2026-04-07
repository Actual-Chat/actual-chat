using ActualChat.App.Server.Flows;
using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Media.IntegrationTests.Flows;

[Collection(nameof(MediaCollection))]
public class IconSvgToPngMigrationFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string ReplacesMediaIdKey = "ReplacesMediaId";

    private static readonly byte[] TestSvgBytes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
          <circle cx="50" cy="50" r="40" fill="blue"/>
        </svg>
        """u8.ToArray();

    private IBlobStorage BlobStorage { get; } = fixture.AppHost.Services.BlobStorages()[BlobScope.ContentRecord];
    private IMediaBackend MediaBackend { get; } = fixture.AppHost.Services.GetRequiredService<IMediaBackend>();
    private DbHub<UsersDbContext> UsersDbHub { get; } = fixture.AppHost.Services.DbHub<UsersDbContext>();
    private DbHub<ChatDbContext> ChatDbHub { get; } = fixture.AppHost.Services.DbHub<ChatDbContext>();

    [Fact]
    public async Task ShouldConvertSvgMediaToPng()
    {
        // arrange: seed an SVG media record + an Avatar that references it
        var (svgMediaId, svgBlobId) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var avatarId = await SeedAvatar(svgMediaId);

        // act
        await RunFlow();

        // assert: the avatar now points at a *new* MediaId whose blob is PNG
        await AssertFlow(async ct => {
            var newMediaId = await GetAvatarMediaId(avatarId, ct);
            newMediaId.Should().NotBeNull();
            newMediaId!.Value.Should().NotBe(svgMediaId.Value);

            var pngMedia = await MediaBackend.GetFull(newMediaId, ct);
            pngMedia.Should().NotBeNull();
            pngMedia!.ContentType.Should().Be("image/png");
            pngMedia.BlobId.Should().EndWith(".png");
            pngMedia.Width.Should().Be(Constants.Attachments.MaxIconSize);
            pngMedia.Height.Should().Be(Constants.Attachments.MaxIconSize);
        });

        // assert: PNG blob exists, original SVG blob is preserved
        var newAvatarMediaId = await GetAvatarMediaId(avatarId, CancellationToken.None);
        var pngRow = await MediaBackend.GetFull(newAvatarMediaId, CancellationToken.None);
        await AssertBlobExists(pngRow!.BlobId);
        await AssertBlobExists(svgBlobId);
    }

    [Fact]
    public async Task ShouldStoreReplacesMediaIdOnNewPngRow()
    {
        // arrange
        var (svgMediaId, _) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var avatarId = await SeedAvatar(svgMediaId);

        // act
        await RunFlow();

        // assert: the new PNG row carries ReplacesMediaId pointing back at the original SVG MediaId
        await AssertFlow(async ct => {
            var newMediaId = await GetAvatarMediaId(avatarId, ct);
            newMediaId.Should().NotBeNull();
            var pngMedia = await MediaBackend.GetFull(newMediaId, ct);
            pngMedia.Should().NotBeNull();
            var replacesMediaId = pngMedia!.Metadata[ReplacesMediaIdKey];
            replacesMediaId.Should().Be(svgMediaId.Value);
        });
    }

    [Fact]
    public async Task ShouldLeaveOriginalSvgRowAndBlobIntact()
    {
        // arrange
        var (svgMediaId, svgBlobId) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        await SeedAvatar(svgMediaId);
        var originalSvg = await MediaBackend.GetFull(svgMediaId, CancellationToken.None);
        originalSvg.Should().NotBeNull();
        var originalVersion = originalSvg!.Version;

        // act
        await RunFlow();

        // assert: the original SVG Media row is byte-for-byte intact and the blob still exists
        await AssertFlow(async ct => {
            var stillSvg = await MediaBackend.GetFull(svgMediaId, ct);
            stillSvg.Should().NotBeNull();
            stillSvg!.BlobId.Should().Be(svgBlobId);
            stillSvg.ContentType.Should().Be("image/svg+xml");
            stillSvg.FileName.Should().Be("avatar.svg");
            stillSvg.Width.Should().Be(100);
            stillSvg.Height.Should().Be(100);
            stillSvg.Length.Should().Be(TestSvgBytes.Length);
            stillSvg.Kind.Should().Be(MediaKind.UserAvatarPicture);
            stillSvg.Version.Should().Be(originalVersion);
        });
        await AssertBlobExists(svgBlobId);
    }

    [Fact]
    public async Task ShouldBeIdempotentOnRerun()
    {
        // arrange
        var (svgMediaId, _) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var avatarId = await SeedAvatar(svgMediaId);

        // act 1
        await RunFlow();
        MediaId? mediaIdAfterFirstRun = null;
        await AssertFlow(async ct => {
            mediaIdAfterFirstRun = await GetAvatarMediaId(avatarId, ct);
            mediaIdAfterFirstRun.Should().NotBeNull();
            mediaIdAfterFirstRun!.Value.Should().NotBe(svgMediaId.Value);
        });

        // act 2 — re-run from a clean flow state
        await RunFlow();

        // assert: the avatar's MediaId is unchanged and the second run converted nothing
        await AssertFlow(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.ConvertedCount.Should().Be(0);

            var mediaIdAfterSecondRun = await GetAvatarMediaId(avatarId, ct);
            mediaIdAfterSecondRun.Should().Be(mediaIdAfterFirstRun);
        });
    }

    [Fact]
    public async Task ShouldSkipMediaReferencedByAttachments()
    {
        // arrange: SVG media referenced by both an avatar and a chat-entry attachment
        var (svgMediaId, svgBlobId) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.UserAvatarPicture, "shared.svg");
        var avatarId = await SeedAvatar(svgMediaId);
        await SeedChatEntryAttachment(svgMediaId);

        // act
        await RunFlow();

        // assert: avatar still points at the original SVG MediaId, original row untouched
        await AssertFlow(async ct => {
            var mediaIdAfter = await GetAvatarMediaId(avatarId, ct);
            mediaIdAfter.Should().Be(svgMediaId);

            var stillSvg = await MediaBackend.GetFull(svgMediaId, ct);
            stillSvg.Should().NotBeNull();
            stillSvg!.BlobId.Should().Be(svgBlobId);
            stillSvg.ContentType.Should().Be("image/svg+xml");

            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow!.ConvertedCount.Should().Be(0);
            flow.SkippedCount.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task ShouldSkipNonSvgMedia()
    {
        // arrange: seed a PNG media record + an Avatar that references it
        var pngBytes = TestImages.CreatePng(50, 50);
        var (pngMediaId, pngBlobId) = await CreateMedia(
            pngBytes, ".png", "image/png", MediaKind.UserAvatarPicture, "avatar.png", width: 50, height: 50);
        var avatarId = await SeedAvatar(pngMediaId);

        // act
        await RunFlow();

        // assert: flow completes and the avatar still points at the original PNG media
        await AssertFlow(async ct => {
            var mediaIdAfter = await GetAvatarMediaId(avatarId, ct);
            mediaIdAfter.Should().Be(pngMediaId);

            var unchanged = await MediaBackend.GetFull(pngMediaId, ct);
            unchanged.Should().NotBeNull();
            unchanged!.ContentType.Should().Be("image/png");
            unchanged.BlobId.Should().Be(pngBlobId);
        });
    }

    [Fact]
    public async Task ShouldHandleMissingBlob()
    {
        // arrange: seed an SVG media record (without writing the blob) + a Chat that references it
        var (svgMediaId, svgBlobId) = await CreateMedia(
            blobBytes: null, ".svg", "image/svg+xml", MediaKind.ChatPicture, "missing.svg");
        var chatId = await SeedChat(svgMediaId);

        // act
        await RunFlow();

        // assert: flow completes and the chat still points at the original SVG media (missing blob -> skipped)
        await AssertFlow(async ct => {
            var mediaIdAfter = await GetChatMediaId(chatId, ct);
            mediaIdAfter.Should().Be(svgMediaId);

            var stillSvg = await MediaBackend.GetFull(svgMediaId, ct);
            stillSvg.Should().NotBeNull();
            stillSvg!.ContentType.Should().Be("image/svg+xml");
            stillSvg.BlobId.Should().Be(svgBlobId);
        });
    }

    [Fact]
    public async Task ShouldConvertMediaReferencedByAvatarChatAndPlace()
    {
        // arrange: 3 SVG media records, each referenced by a different entity kind
        var (avatarSvgId, _) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var (chatSvgId, _) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.ChatPicture, "chat.svg");
        var (placeSvgId, _) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.ChatPicture, "place.svg");

        var avatarId = await SeedAvatar(avatarSvgId);
        var chatId = await SeedChat(chatSvgId);
        var placeId = await SeedPlace(placeSvgId);

        // act
        await RunFlow();

        // assert: each host entity now points at a different MediaId whose blob is PNG
        await AssertFlow(async ct => {
            await AssertRepointedToPng(avatarSvgId, await GetAvatarMediaId(avatarId, ct), ct);
            await AssertRepointedToPng(chatSvgId, await GetChatMediaId(chatId, ct), ct);
            await AssertRepointedToPng(placeSvgId, await GetPlaceMediaId(placeId, ct), ct);
        });
    }

    // Private methods

    private async Task AssertRepointedToPng(MediaId originalSvgId, MediaId? newMediaId, CancellationToken ct)
    {
        newMediaId.Should().NotBeNull();
        newMediaId!.Value.Should().NotBe(originalSvgId.Value);
        var pngMedia = await MediaBackend.GetFull(newMediaId, ct);
        pngMedia.Should().NotBeNull();
        pngMedia!.ContentType.Should().Be("image/png");
        pngMedia.BlobId.Should().EndWith(".png");
    }

    private async Task<(MediaId MediaId, string BlobId)> CreateMedia(
        byte[]? blobBytes,
        string extension,
        string contentType,
        MediaKind kind,
        string fileName,
        int width = 100,
        int height = 100)
    {
        var mediaId = MediaId.New("test-chat");
        var blobId = MediaSaver.GetBlobId(mediaId, extension);
        if (blobBytes is not null) {
            using var stream = new MemoryStream(blobBytes);
            await BlobStorage.Write(blobId, stream, contentType, CancellationToken.None);
        }
        var media = new MediaFull(mediaId) {
            Kind = kind,
            BlobId = blobId,
            ContentType = contentType,
            FileName = fileName,
            Width = width,
            Height = height,
            Length = blobBytes?.Length ?? 0,
        };
        await Commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, CancellationToken.None);
        return (mediaId, blobId);
    }

    // Schedules the migration flow with reset.
    private Task RunFlow()
        => FlowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

    // Waits until the flow has completed and `assertion` passes (or times out).
    private Task AssertFlow(Func<CancellationToken, Task> assertion)
        => ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull();
            await assertion(ct).ConfigureAwait(false);
        }, TimeSpan.FromSeconds(30));

    private async Task AssertBlobExists(string blobId)
    {
        var stream = await BlobStorage.Read(blobId, CancellationToken.None);
        stream.Should().NotBeNull();
        await using var _ = stream.ConfigureAwait(false);
        stream.Length.Should().BeGreaterThan(0);
    }

    private async Task<Symbol> SeedAvatar(MediaId mediaId)
    {
        var avatarId = DbAvatar.IdGenerator.Next();
        var dbContext = await UsersDbHub.CreateDbContext(readWrite: true, CancellationToken.None).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        dbContext.Avatars.Add(new DbAvatar {
            Id = avatarId,
            Version = 1,
            UserId = UserId.New().Value,
            Name = "test",
            MediaId = mediaId.Value,
        });
        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return avatarId;
    }

    private async Task<ChatId> SeedChat(MediaId mediaId)
    {
        var dbContext = await ChatDbHub.CreateDbContext(readWrite: true, CancellationToken.None).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var chatId = (ChatId)GroupChatId.New();
        dbContext.Chats.Add(new DbChat {
            Id = chatId.Value,
            Version = 1,
            Title = "test",
            Kind = ChatKind.Group,
            MediaId = mediaId.Value,
            CreatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return chatId;
    }

    private async Task<PlaceId> SeedPlace(MediaId mediaId)
    {
        var dbContext = await ChatDbHub.CreateDbContext(readWrite: true, CancellationToken.None).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var placeId = PlaceId.New();
        dbContext.Places.Add(new DbPlace {
            Id = placeId.Value,
            Version = 1,
            Title = "test",
            MediaId = mediaId.Value,
            CreatedAt = DateTime.UtcNow,
        });
        // PlacesBackend.OnChange propagates place updates to the place's root chat and
        // requires it to exist on Update. Seed it so RepointPlace can run end-to-end.
        var rootChatId = (ChatId)placeId.RootChatId;
        dbContext.Chats.Add(new DbChat {
            Id = rootChatId.Value,
            Version = 1,
            Title = "test",
            Kind = ChatKind.Place,
            CreatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return placeId;
    }

    private async Task SeedChatEntryAttachment(MediaId mediaId)
    {
        var dbContext = await ChatDbHub.CreateDbContext(readWrite: true, CancellationToken.None).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        // The migration's IsReferencedOutsideIcons check only filters by MediaId, so the
        // (otherwise required) entry id can be a placeholder — no FK enforces it.
        var fakeEntryId = $"{GroupChatId.New().Value}:0:0";
        dbContext.ChatEntryAttachments.Add(new DbChatEntryAttachment {
            Id = $"{fakeEntryId}:0",
            Version = 1,
            EntryId = fakeEntryId,
            Index = 0,
            MediaId = mediaId.Value,
        });
        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<MediaId?> GetAvatarMediaId(Symbol avatarId, CancellationToken cancellationToken)
    {
        var dbContext = await UsersDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbAvatar = await dbContext.Avatars
            .FirstOrDefaultAsync(x => x.Id == avatarId.Value, cancellationToken)
            .ConfigureAwait(false);
        return MediaId.ParseNullable(dbAvatar?.MediaId);
    }

    private async Task<MediaId?> GetChatMediaId(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = await ChatDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbChat = await dbContext.Chats
            .FirstOrDefaultAsync(x => x.Id == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        return MediaId.ParseNullable(dbChat?.MediaId);
    }

    private async Task<MediaId?> GetPlaceMediaId(PlaceId placeId, CancellationToken cancellationToken)
    {
        var dbContext = await ChatDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbPlace = await dbContext.Places
            .FirstOrDefaultAsync(x => x.Id == placeId.Value, cancellationToken)
            .ConfigureAwait(false);
        return MediaId.ParseNullable(dbPlace?.MediaId);
    }
}
