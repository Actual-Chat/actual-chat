using ActualChat.App.Server.Flows;
using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media.IntegrationTests.Flows;

public class IconSvgToPngMigrationFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(IconSvgToPngMigrationFlowTest)}", TestAppHostOptions.Default, @out)
{
    private static readonly byte[] TestSvgBytes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
          <circle cx="50" cy="50" r="40" fill="blue"/>
        </svg>
        """u8.ToArray();

    [Fact]
    public async Task ShouldConvertSvgMediaToPng()
    {
        await using var h = await NewAppHost();
        var services = h.Services;
        var commander = services.Commander();
        var blobStorage = services.BlobStorages()[BlobScope.ContentRecord];

        // arrange: seed an SVG media record + an Avatar that references it
        var mediaId = MediaId.New("test-chat");
        var svgBlobId = MediaSaver.GetBlobId(mediaId, ".svg");
        using (var svgStream = new MemoryStream(TestSvgBytes))
            await blobStorage.Write(svgBlobId, svgStream, "image/svg+xml", default);

        var media = new MediaFull(mediaId) {
            Kind = MediaKind.UserAvatarPicture,
            BlobId = svgBlobId,
            ContentType = "image/svg+xml",
            FileName = "avatar.svg",
            Width = 100,
            Height = 100,
            Length = TestSvgBytes.Length,
        };
        await commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, default);
        await SeedAvatar(services, mediaId, default);

        // act
        var flowHub = services.FlowHub();
        await flowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert: flow completes and the test media was converted
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.UntypedResult.Should().NotBeNull();
            var updated = await mediaBackend.GetFull(mediaId, ct);
            updated.Should().NotBeNull();
            updated!.ContentType.Should().Be("image/png");
            updated.BlobId.Should().NotBe(svgBlobId);
            updated.BlobId.Should().EndWith(".png");
            updated.Width.Should().Be(100);
            updated.Height.Should().Be(100);
        }, TimeSpan.FromSeconds(30));

        // assert: PNG blob exists, old SVG blob is preserved
        var current = await mediaBackend.GetFull(mediaId, default);
        var pngStream = await blobStorage.Read(current!.BlobId, default);
        pngStream.Should().NotBeNull();
        await using (pngStream!.ConfigureAwait(false))
            pngStream.Length.Should().BeGreaterThan(0);
        var oldBlob = await blobStorage.Read(svgBlobId, default);
        oldBlob.Should().NotBeNull();
        await using (oldBlob!.ConfigureAwait(false)) { }
    }

    [Fact]
    public async Task ShouldSkipNonSvgMedia()
    {
        await using var h = await NewAppHost();
        var services = h.Services;
        var commander = services.Commander();
        var blobStorage = services.BlobStorages()[BlobScope.ContentRecord];

        // arrange: seed a PNG media record + an Avatar that references it
        var pngBytes = TestImages.CreatePng(50, 50);
        var mediaId = MediaId.New("test-chat");
        var pngBlobId = MediaSaver.GetBlobId(mediaId, ".png");
        using (var pngStream = new MemoryStream(pngBytes))
            await blobStorage.Write(pngBlobId, pngStream, "image/png", default);

        var media = new MediaFull(mediaId) {
            Kind = MediaKind.UserAvatarPicture,
            BlobId = pngBlobId,
            ContentType = "image/png",
            FileName = "avatar.png",
            Width = 50,
            Height = 50,
            Length = pngBytes.Length,
        };
        await commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, default);
        await SeedAvatar(services, mediaId, default);

        // act
        var flowHub = services.FlowHub();
        await flowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert: flow completes and the test PNG media is left untouched
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.UntypedResult.Should().NotBeNull();
            var unchanged = await mediaBackend.GetFull(mediaId, ct);
            unchanged.Should().NotBeNull();
            unchanged!.ContentType.Should().Be("image/png");
            unchanged.BlobId.Should().Be(pngBlobId);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldHandleMissingBlob()
    {
        await using var h = await NewAppHost();
        var services = h.Services;
        var commander = services.Commander();

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
        await commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, default);
        await SeedChat(services, mediaId, default);

        // act
        var flowHub = services.FlowHub();
        await flowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert: flow completes and the test media is left as SVG (missing blob -> skipped)
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.UntypedResult.Should().NotBeNull();
            var unchanged = await mediaBackend.GetFull(mediaId, ct);
            unchanged.Should().NotBeNull();
            unchanged!.ContentType.Should().Be("image/svg+xml");
            unchanged.BlobId.Should().Be(svgBlobId);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldConvertMediaReferencedByAvatarChatAndPlace()
    {
        await using var h = await NewAppHost();
        var services = h.Services;
        var commander = services.Commander();
        var blobStorage = services.BlobStorages()[BlobScope.ContentRecord];

        // arrange: 3 SVG media records, each referenced by a different entity kind
        var avatarMediaId = await SeedSvgMedia(services, "avatar.svg");
        var chatMediaId = await SeedSvgMedia(services, "chat.svg");
        var placeMediaId = await SeedSvgMedia(services, "place.svg");

        await SeedAvatar(services, avatarMediaId, default);
        await SeedChat(services, chatMediaId, default);
        await SeedPlace(services, placeMediaId, default);

        // act
        var flowHub = services.FlowHub();
        await flowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert: flow completes and all 3 test media records were converted
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var mediaIds = new[] { avatarMediaId, chatMediaId, placeMediaId };
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.UntypedResult.Should().NotBeNull();
            foreach (var mediaId in mediaIds) {
                var updated = await mediaBackend.GetFull(mediaId, ct);
                updated.Should().NotBeNull();
                updated!.ContentType.Should().Be("image/png");
                updated.BlobId.Should().EndWith(".png");
            }
        }, TimeSpan.FromSeconds(30));
    }

    // Private methods

    private static async Task<MediaId> SeedSvgMedia(IServiceProvider services, string fileName)
    {
        var commander = services.Commander();
        var blobStorage = services.BlobStorages()[BlobScope.ContentRecord];

        var mediaId = MediaId.New("test-chat");
        var svgBlobId = MediaSaver.GetBlobId(mediaId, ".svg");
        using (var svgStream = new MemoryStream(TestSvgBytes))
            await blobStorage.Write(svgBlobId, svgStream, "image/svg+xml", default);

        var media = new MediaFull(mediaId) {
            Kind = MediaKind.ChatPicture,
            BlobId = svgBlobId,
            ContentType = "image/svg+xml",
            FileName = fileName,
            Width = 100,
            Height = 100,
            Length = TestSvgBytes.Length,
        };
        await commander.Call(
            new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
            true, default);
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
