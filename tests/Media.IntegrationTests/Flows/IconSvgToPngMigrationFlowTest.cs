using ActualChat.Media.Db;
using ActualChat.App.Server.Flows;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

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

        // arrange
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

        // act
        var flowHub = services.FlowHub();
        await flowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.UntypedResult.Should().NotBeNull();
            flow.ConvertedCount.Should().Be(1);
        }, TimeSpan.FromSeconds(30));

        // assert: media record was updated
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var updated = await mediaBackend.GetFull(mediaId, default);
        updated.Should().NotBeNull();
        updated!.ContentType.Should().Be("image/png");
        updated.BlobId.Should().NotBe(svgBlobId);
        updated.BlobId.Should().EndWith(".png");
        updated.Width.Should().Be(100);
        updated.Height.Should().Be(100);

        // assert: PNG blob exists
        var pngStream = await blobStorage.Read(updated.BlobId, default);
        pngStream.Should().NotBeNull();
        await using (pngStream!.ConfigureAwait(false)) {
            pngStream.Length.Should().BeGreaterThan(0);
        }

        // assert: old SVG blob is preserved
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

        // arrange
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

        // act
        var flowHub = services.FlowHub();
        await flowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.UntypedResult.Should().NotBeNull();
            flow.ConvertedCount.Should().Be(0);
        }, TimeSpan.FromSeconds(30));

        // assert: media record is unchanged
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var unchanged = await mediaBackend.GetFull(mediaId, default);
        unchanged.Should().NotBeNull();
        unchanged!.ContentType.Should().Be("image/png");
        unchanged.BlobId.Should().Be(pngBlobId);
    }

    [Fact]
    public async Task ShouldHandleMissingBlob()
    {
        await using var h = await NewAppHost();
        var services = h.Services;
        var commander = services.Commander();

        // arrange
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

        // act
        var flowHub = services.FlowHub();
        await flowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.UntypedResult.Should().NotBeNull();
            flow.ConvertedCount.Should().Be(0);
            flow.SkippedCount.Should().Be(1);
        }, TimeSpan.FromSeconds(30));

        // assert: media record is unchanged (still SVG)
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var unchanged = await mediaBackend.GetFull(mediaId, default);
        unchanged.Should().NotBeNull();
        unchanged!.ContentType.Should().Be("image/svg+xml");
    }

    [Fact]
    public async Task ShouldConvertMultipleMediaKinds()
    {
        await using var h = await NewAppHost();
        var services = h.Services;
        var commander = services.Commander();
        var blobStorage = services.BlobStorages()[BlobScope.ContentRecord];

        // arrange
        var kinds = new[] { MediaKind.ChatPicture, MediaKind.UserPicture, MediaKind.UserAvatarPicture };
        var mediaIds = new List<MediaId>();

        foreach (var kind in kinds) {
            var mediaId = MediaId.New("test-chat");
            mediaIds.Add(mediaId);

            var svgBlobId = MediaSaver.GetBlobId(mediaId, ".svg");
            using (var svgStream = new MemoryStream(TestSvgBytes))
                await blobStorage.Write(svgBlobId, svgStream, "image/svg+xml", default);

            var media = new MediaFull(mediaId) {
                Kind = kind,
                BlobId = svgBlobId,
                ContentType = "image/svg+xml",
                FileName = $"icon-{kind}.svg",
                Width = 100,
                Height = 100,
                Length = TestSvgBytes.Length,
            };
            await commander.Call(
                new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media }),
                true, default);
        }

        // act
        var flowHub = services.FlowHub();
        await flowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

        // assert
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow!.UntypedResult.Should().NotBeNull();
            flow.ConvertedCount.Should().Be(3);
        }, TimeSpan.FromSeconds(30));

        // assert: all records updated
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        foreach (var mediaId in mediaIds) {
            var updated = await mediaBackend.GetFull(mediaId, default);
            updated.Should().NotBeNull();
            updated!.ContentType.Should().Be("image/png");
            updated.BlobId.Should().EndWith(".png");
        }
    }
}
