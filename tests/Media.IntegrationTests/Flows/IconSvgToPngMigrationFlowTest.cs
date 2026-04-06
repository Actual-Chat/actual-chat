using ActualChat.App.Server.Flows;
using ActualChat.Chat.Db;
using ActualChat.Media.Db;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

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
    private DbHub<MediaDbContext> MediaDbHub { get; } = fixture.AppHost.Services.DbHub<MediaDbContext>();
    private IServiceProvider Services { get; } = fixture.AppHost.Services;

    [Fact]
    public async Task ShouldConvertSvgMediaToPng()
    {
        // arrange: seed an SVG media record + an Avatar that references it
        var mediaId = MediaId.New("test-chat");
        var svgBlobId = MediaSaver.GetBlobId(mediaId, ".svg");
        using (var svgStream = new MemoryStream(TestSvgBytes))
            await BlobStorage.Write(svgBlobId, svgStream, "image/svg+xml", CancellationToken.None);

        var media = new MediaFull(mediaId) {
            Kind = MediaKind.UserAvatarPicture,
            BlobId = svgBlobId,
            ContentType = "image/svg+xml",
            FileName = "avatar.svg",
            Width = 100,
            Height = 100,
            Length = TestSvgBytes.Length,
        };
        await Commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, CancellationToken.None);
        await SeedAvatar(Services, mediaId, CancellationToken.None);

        // act
        await FlowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert: flow completes and the test media was converted
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull();
            var updated = await GetDbMedia(mediaId, ct);
            updated.Should().NotBeNull();
            updated!.ContentType.Should().Be("image/png");
            updated.BlobId.Should().NotBe(svgBlobId);
            updated.BlobId.Should().EndWith(".png");
            updated.Width.Should().Be(100);
            updated.Height.Should().Be(100);
        }, TimeSpan.FromSeconds(30));

        // assert: PNG blob exists, old SVG blob is preserved
        var current = await GetDbMedia(mediaId, CancellationToken.None);
        var pngStream = await BlobStorage.Read(current!.BlobId, CancellationToken.None);
        pngStream.Should().NotBeNull();
        await using (pngStream.ConfigureAwait(false))
            pngStream.Length.Should().BeGreaterThan(0);
        var oldBlob = await BlobStorage.Read(svgBlobId, CancellationToken.None);
        oldBlob.Should().NotBeNull();
        await using (oldBlob.ConfigureAwait(false)) { }
    }

    [Fact]
    public async Task ShouldSkipNonSvgMedia()
    {
        // arrange: seed a PNG media record + an Avatar that references it
        var pngBytes = TestImages.CreatePng(50, 50);
        var mediaId = MediaId.New("test-chat");
        var pngBlobId = MediaSaver.GetBlobId(mediaId, ".png");
        using (var pngStream = new MemoryStream(pngBytes))
            await BlobStorage.Write(pngBlobId, pngStream, "image/png", CancellationToken.None);

        var media = new MediaFull(mediaId) {
            Kind = MediaKind.UserAvatarPicture,
            BlobId = pngBlobId,
            ContentType = "image/png",
            FileName = "avatar.png",
            Width = 50,
            Height = 50,
            Length = pngBytes.Length,
        };
        await Commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, CancellationToken.None);
        await SeedAvatar(Services, mediaId, CancellationToken.None);

        // act
        await FlowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert: flow completes and the test PNG media is left untouched
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull();
            var unchanged = await GetDbMedia(mediaId, ct);
            unchanged.Should().NotBeNull();
            unchanged!.ContentType.Should().Be("image/png");
            unchanged.BlobId.Should().Be(pngBlobId);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldHandleMissingBlob()
    {
        // arrange: seed an SVG media record (without writing the blob) + a Chat that references it
        var mediaId = MediaId.New("test-chat");
        var svgBlobId = MediaSaver.GetBlobId(mediaId, ".svg");
        // Deliberately do NOT write the blob

        var media = new MediaFull(mediaId) {
            Kind = MediaKind.ChatPicture,
            BlobId = svgBlobId,
            ContentType = "image/svg+xml",
            FileName = "missing.svg",
            Length = 0,
        };
        await Commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, CancellationToken.None);
        await SeedChat(Services, mediaId, CancellationToken.None);

        // act
        await FlowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert: flow completes and the test media is left as SVG (missing blob -> skipped)
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull();
            var unchanged = await GetDbMedia(mediaId, ct);
            unchanged.Should().NotBeNull();
            unchanged!.ContentType.Should().Be("image/svg+xml");
            unchanged.BlobId.Should().Be(svgBlobId);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldConvertMediaReferencedByAvatarChatAndPlace()
    {
        // arrange: 3 SVG media records, each referenced by a different entity kind
        var avatarMediaId = await SeedSvgMedia("avatar.svg");
        var chatMediaId = await SeedSvgMedia("chat.svg");
        var placeMediaId = await SeedSvgMedia("place.svg");

        await SeedAvatar(Services, avatarMediaId, CancellationToken.None);
        await SeedChat(Services, chatMediaId, CancellationToken.None);
        await SeedPlace(Services, placeMediaId, CancellationToken.None);

        // act
        await FlowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert: flow completes and all 3 test media records were converted
        var mediaIds = new[] { avatarMediaId, chatMediaId, placeMediaId };
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull();
            foreach (var mediaId in mediaIds) {
                var updated = await GetDbMedia(mediaId, ct);
                updated.Should().NotBeNull();
                updated!.ContentType.Should().Be("image/png");
                updated.BlobId.Should().EndWith(".png");
            }
        }, TimeSpan.FromSeconds(30));
    }

    // Private methods

    // Reads DbMedia directly (bypasses Fusion cache), because the flow writes via plain
    // SaveChangesAsync which doesn't trigger MediaBackend.GetFull invalidation.
    private async Task<MediaFull?> GetDbMedia(MediaId mediaId, CancellationToken cancellationToken)
    {
        var dbContext = await MediaDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbMedia = await dbContext.Media.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == mediaId.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbMedia?.ToModel();
    }

    private async Task<MediaId> SeedSvgMedia(string fileName)
    {
        var mediaId = MediaId.New("test-chat");
        var svgBlobId = MediaSaver.GetBlobId(mediaId, ".svg");
        using (var svgStream = new MemoryStream(TestSvgBytes))
            await BlobStorage.Write(svgBlobId, svgStream, "image/svg+xml", CancellationToken.None);

        var media = new MediaFull(mediaId) {
            Kind = MediaKind.ChatPicture,
            BlobId = svgBlobId,
            ContentType = "image/svg+xml",
            FileName = fileName,
            Width = 100,
            Height = 100,
            Length = TestSvgBytes.Length,
        };
        await Commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, CancellationToken.None);
        return mediaId;
    }

    private static async Task SeedAvatar(IServiceProvider services, MediaId mediaId, CancellationToken cancellationToken)
    {
        var dbHub = services.GetRequiredService<DbHub<UsersDbContext>>();
        var dbContext = await dbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        dbContext.Avatars.Add(new DbAvatar {
            Id = DbAvatar.IdGenerator.Next(),
            Version = 1,
            UserId = UserId.New().Value,
            Name = "test",
            MediaId = mediaId.Value,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedChat(IServiceProvider services, MediaId mediaId, CancellationToken cancellationToken)
    {
        var dbHub = services.GetRequiredService<DbHub<ChatDbContext>>();
        var dbContext = await dbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
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
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedPlace(IServiceProvider services, MediaId mediaId, CancellationToken cancellationToken)
    {
        var dbHub = services.GetRequiredService<DbHub<ChatDbContext>>();
        var dbContext = await dbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var placeId = PlaceId.New();
        dbContext.Places.Add(new DbPlace {
            Id = placeId.Value,
            Version = 1,
            Title = "test",
            MediaId = mediaId.Value,
            CreatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
