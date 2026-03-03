using ActualChat.Media;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class MediaUploadFlowTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ShouldReserveMediaAndUploadFile()
    {
        // Arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        var account = await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var mediaProgressBackend = services.GetRequiredService<IMediaProgressBackend>();
        var commander = tester.Commander;

        var scope = "test-scope";
        var testData = "Hello, this is test file content!"u8.ToArray();

        // Act 1: Reserve MediaId
        var mediaId = await commander.Call(new Medias_ReserveMedia(session, scope));

        // Assert 1: MediaId is created and status is Reserved
        mediaId.Should().NotBeNull();
        mediaId.Scope.Should().Be(scope);

        var media = await mediaBackend.GetFull(mediaId, default);
        media.Should().NotBeNull();
        media.UserId.Should().Be(account.Id);

        var progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().NotBeNull();
        progress!.Stage.Should().Be(MediaStage.Reserved);

        // Act 2: Create Upload
        var metadata = new PropertyBag()
            .Set("FileName", "test.txt")
            .Set("ContentType", "text/plain");
        var tag = $"MediaUploadTest/v1/{scope}";
        var uploadId = await commander.Call(new Uploads_Create(session, testData.Length, tag, metadata));

        uploadId.Should().NotBeNull();

        // Act 3: Upload file content
        var newOffset = await commander.Call(new Uploads_Append(session, uploadId, 0, testData));

        newOffset.Should().Be(testData.Length);

        // Act 4: Update progress to Uploading
        await commander.Call(new Medias_UpdateProgress(session, mediaId, null, MediaStage.Uploading, 100, ""));

        progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().NotBeNull();
        progress.Stage.Should().Be(MediaStage.Uploading);
        progress.StageProgress.Should().Be(100);

        // Act 5: Process upload - verifies upload is complete, runs processors, saves ContentId to Media, updates progress to Ready, removes upload
        var mediaContent = await commander.Call(new Medias_ProcessUpload(session, mediaId, uploadId));

        mediaContent.Should().NotBeNull();
        mediaContent.BlobId.Should().NotBeNullOrEmpty();

        // Verify media has ContentId
        media = await mediaBackend.GetFull(mediaId, default);
        media.Should().NotBeNull();
        media.BlobId.Should().Be(mediaContent.BlobId);

        // Verify progress is Ready
        progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().NotBeNull();
        progress.Stage.Should().Be(MediaStage.Ready);

        // Verify upload is removed (should throw or return null)
        var uploads = services.GetRequiredService<IUploads>();
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            await uploads.GetOffset(session, uploadId, default);
        });
    }

    [Fact]
    public async Task ShouldAllowOnlyOwnerToUpdateProgress()
    {
        // Arrange
        await using var ownerTester = AppHost.NewBlazorTester(Out);
        var ownerSession = ownerTester.Session;
        await ownerTester.SignInAsUniqueBob();

        var services = ownerTester.AppServices;
        var medias = services.GetRequiredService<IMedias>();
        var commander = ownerTester.Commander;

        // Owner reserves media
        var mediaId = await commander.Call(new Medias_ReserveMedia(ownerSession, "owner-test"));
        mediaId.Should().NotBeNull();

        // Act & Assert: Another user should not be able to update progress
        await using var otherTester = AppHost.NewBlazorTester(Out);
        var otherSession = otherTester.Session;
        await otherTester.SignInAsUniqueAlice();

        var otherCommander = otherTester.Commander;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => {
            await otherCommander.Call(new Medias_UpdateProgress(otherSession, mediaId, null, MediaStage.Ready, 100, ""));
        });
    }

    [Fact]
    public async Task ShouldAllowOnlyOwnerToRemoveMedia()
    {
        // Arrange
        await using var ownerTester = AppHost.NewBlazorTester(Out);
        var ownerSession = ownerTester.Session;
        await ownerTester.SignInAsUniqueBob();

        var services = ownerTester.AppServices;
        var commander = ownerTester.Commander;

        // Owner reserves media
        var mediaId = await commander.Call(new Medias_ReserveMedia(ownerSession, "remove-test"));

        // Act & Assert: Another user should not be able to remove
        await using var otherTester = AppHost.NewBlazorTester(Out);
        var otherSession = otherTester.Session;
        await otherTester.SignInAsUniqueAlice();

        var otherCommander = otherTester.Commander;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => {
            await otherCommander.Call(new Medias_RemoveMedia(otherSession, mediaId));
        });

        // Owner can remove
        await commander.Call(new Medias_RemoveMedia(ownerSession, mediaId));

        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var media = await mediaBackend.GetFull(mediaId, default);
        media.Should().BeNull();
    }

    [Fact]
    public async Task ShouldRemoveMediaAndProgress()
    {
        // Arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var mediaProgressBackend = services.GetRequiredService<IMediaProgressBackend>();
        var commander = tester.Commander;

        // Reserve media
        var mediaId = await commander.Call(new Medias_ReserveMedia(session, "delete-test"));

        // Verify both media and progress exist
        var media = await mediaBackend.GetFull(mediaId, default);
        media.Should().NotBeNull();

        var progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().NotBeNull();

        // Act: Remove media
        await commander.Call(new Medias_RemoveMedia(session, mediaId));

        // Assert: Both media and progress are removed
        media = await mediaBackend.GetFull(mediaId, default);
        media.Should().BeNull();

        progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().BeNull();
    }
}
