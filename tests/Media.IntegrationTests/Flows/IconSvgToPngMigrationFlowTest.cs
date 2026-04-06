using ActualChat.App.Server.Flows;
using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media.IntegrationTests.Flows;

[Collection(nameof(MediaCollection))]
public class IconSvgToPngMigrationFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
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
        var (mediaId, svgBlobId) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        await SeedAvatar(mediaId);

        // act & assert: flow converts the media to PNG
        await RunFlowAndAssert(async ct => {
            var updated = await MediaBackend.GetFull(mediaId, ct);
            updated.Should().NotBeNull();
            updated.ContentType.Should().Be("image/png");
            updated.BlobId.Should().NotBe(svgBlobId);
            updated.BlobId.Should().EndWith(".png");
            updated.Width.Should().Be(Constants.Attachments.MaxIconSize);
            updated.Height.Should().Be(Constants.Attachments.MaxIconSize);
        });

        // assert: PNG blob exists, old SVG blob is preserved
        var current = await MediaBackend.GetFull(mediaId, CancellationToken.None);
        await AssertBlobExists(current!.BlobId);
        await AssertBlobExists(svgBlobId);
    }

    [Fact]
    public async Task ShouldSkipNonSvgMedia()
    {
        // arrange: seed a PNG media record + an Avatar that references it
        var pngBytes = TestImages.CreatePng(50, 50);
        var (mediaId, pngBlobId) = await CreateMedia(
            pngBytes, ".png", "image/png", MediaKind.UserAvatarPicture, "avatar.png", width: 50, height: 50);
        await SeedAvatar(mediaId);

        // act & assert: flow completes and the PNG media is left untouched
        await RunFlowAndAssert(async ct => {
            var unchanged = await MediaBackend.GetFull(mediaId, ct);
            unchanged.Should().NotBeNull();
            unchanged!.ContentType.Should().Be("image/png");
            unchanged.BlobId.Should().Be(pngBlobId);
        });
    }

    [Fact]
    public async Task ShouldHandleMissingBlob()
    {
        // arrange: seed an SVG media record (without writing the blob) + a Chat that references it
        var (mediaId, svgBlobId) = await CreateMedia(
            blobBytes: null, ".svg", "image/svg+xml", MediaKind.ChatPicture, "missing.svg");
        await SeedChat(mediaId);

        // act & assert: flow completes and the media is left as SVG (missing blob -> skipped)
        await RunFlowAndAssert(async ct => {
            var unchanged = await MediaBackend.GetFull(mediaId, ct);
            unchanged.Should().NotBeNull();
            unchanged!.ContentType.Should().Be("image/svg+xml");
            unchanged.BlobId.Should().Be(svgBlobId);
        });
    }

    [Fact]
    public async Task ShouldConvertMediaReferencedByAvatarChatAndPlace()
    {
        // arrange: 3 SVG media records, each referenced by a different entity kind
        var (avatarMediaId, _) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var (chatMediaId, _) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.ChatPicture, "chat.svg");
        var (placeMediaId, _) = await CreateMedia(
            TestSvgBytes, ".svg", "image/svg+xml", MediaKind.ChatPicture, "place.svg");

        await SeedAvatar(avatarMediaId);
        await SeedChat(chatMediaId);
        await SeedPlace(placeMediaId);

        // act & assert: flow completes and all 3 media records were converted
        var mediaIds = new[] { avatarMediaId, chatMediaId, placeMediaId };
        await RunFlowAndAssert(async ct => {
            foreach (var mediaId in mediaIds) {
                var updated = await MediaBackend.GetFull(mediaId, ct);
                updated.Should().NotBeNull();
                updated!.ContentType.Should().Be("image/png");
                updated.BlobId.Should().EndWith(".png");
            }
        });
    }

    // Private methods

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

    // Schedules the migration flow with reset, then retries `assertion` (plus
    // standard "flow completed" checks) until it passes or times out.
    private async Task RunFlowAndAssert(Func<CancellationToken, Task> assertion)
    {
        await FlowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull();
            await assertion(ct).ConfigureAwait(false);
        }, TimeSpan.FromSeconds(30));
    }

    private async Task AssertBlobExists(string blobId)
    {
        var stream = await BlobStorage.Read(blobId, CancellationToken.None);
        stream.Should().NotBeNull();
        await using var _ = stream!.ConfigureAwait(false);
        stream.Length.Should().BeGreaterThan(0);
    }

    private async Task SeedAvatar(MediaId mediaId)
    {
        var dbContext = await UsersDbHub.CreateDbContext(readWrite: true, CancellationToken.None).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        dbContext.Avatars.Add(new DbAvatar {
            Id = DbAvatar.IdGenerator.Next(),
            Version = 1,
            UserId = UserId.New().Value,
            Name = "test",
            MediaId = mediaId.Value,
        });
        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SeedChat(MediaId mediaId)
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
    }

    private async Task SeedPlace(MediaId mediaId)
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
        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
