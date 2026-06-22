using ActualChat.Testing.Host;
using ActualLab.Rpc;

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
        var mediaId = await commander.Call(new Media_ReserveMedia(session, scope));

        // Assert 1: MediaId is created and status is Reserved
        mediaId.Should().NotBeNull();
        mediaId.Scope.Should().Be(scope);

        var media = await mediaBackend.GetFull(mediaId, default);
        media.Should().NotBeNull();
        media.UserId.Should().Be(account.Id);

        var progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().NotBeNull();
        progress!.Stage.Should().Be(MediaProcessingStage.Reserved);

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
        await commander.Call(new Media_UpdateProgress(session, mediaId, null, MediaProcessingStage.Uploading, 100));

        progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().NotBeNull();
        progress.Stage.Should().Be(MediaProcessingStage.Uploading);
        progress.StageProgress.Should().Be(100);

        // Act 5: Process upload - verifies upload is complete, runs processors, saves ContentId to Media, updates progress to Ready, removes upload
        var mediaRef = await commander.Call(new Media_ProcessUpload(session, mediaId, uploadId));

        mediaRef.Should().NotBeNull();
        mediaRef.BlobId.Should().NotBeNullOrEmpty();

        // Verify media has ContentId
        media = await mediaBackend.GetFull(mediaId, default);
        media.Should().NotBeNull();
        media.BlobId.Should().Be(mediaRef.BlobId);

        // Verify progress is Ready
        progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().NotBeNull();
        progress.Stage.Should().Be(MediaProcessingStage.Ready);

        // Verify upload is removed (should throw or return null)
        var uploads = services.GetRequiredService<IUploads>();
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            await uploads.GetOffset(session, uploadId, default);
        });
    }

    [Fact]
    public async Task ShouldUploadFileViaStream()
    {
        // Arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var commander = tester.Commander;
        var uploads = services.GetRequiredService<IUploads>();

        var scope = "stream-scope";
        // 2.5 MB exercises two full 1 MB flush blocks + a 0.5 MB tail
        var testData = new byte[(5 * 1024 * 1024) / 2];
        for (var i = 0; i < testData.Length; i++)
            testData[i] = (byte)(i * 31 + 7);

        var mediaId = await commander.Call(new Media_ReserveMedia(session, scope));
        var metadata = new PropertyBag()
            .Set("FileName", "test.bin")
            .Set("ContentType", "application/octet-stream");
        var tag = $"MediaUploadStreamTest/v1/{scope}";
        var uploadId = await commander.Call(new Uploads_Create(session, testData.Length, tag, metadata));

        // Act: stream the data in small sub-chunks
        var newOffset = await uploads.AppendStream(session, uploadId, 0, ToRpcStream(testData, 64 * 1024), default);

        // Assert: server accumulated all bytes
        newOffset.Should().Be(testData.Length);
        var offset = await uploads.GetOffset(session, uploadId, default);
        offset.Should().Be(testData.Length);

        // Assert: stored content matches byte-for-byte (block assembly is correct)
        var storage = services.GetRequiredService<UploadsStorage>();
        var stored = await storage.GetDataFile(uploadId, default);
        await using (stored.ConfigureAwait(false)) {
            using var ms = new MemoryStream();
            await stored.CopyToAsync(ms);
            ms.ToArray().Should().Equal(testData);
        }
    }

    [Fact]
    public async Task ShouldAllowOnlyOwnerToUpdateProgress()
    {
        // Arrange
        await using var ownerTester = AppHost.NewBlazorTester(Out);
        var ownerSession = ownerTester.Session;
        await ownerTester.SignInAsUniqueBob();

        var services = ownerTester.AppServices;
        var medias = services.GetRequiredService<IMedia>();
        var commander = ownerTester.Commander;

        // Owner reserves media
        var mediaId = await commander.Call(new Media_ReserveMedia(ownerSession, "owner-test"));
        mediaId.Should().NotBeNull();

        // Act & Assert: Another user should not be able to update progress
        await using var otherTester = AppHost.NewBlazorTester(Out);
        var otherSession = otherTester.Session;
        await otherTester.SignInAsUniqueAlice();

        var otherCommander = otherTester.Commander;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => {
            await otherCommander.Call(new Media_UpdateProgress(otherSession, mediaId, null, MediaProcessingStage.Ready, 100));
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
        var mediaId = await commander.Call(new Media_ReserveMedia(ownerSession, "remove-test"));

        // Act & Assert: Another user should not be able to remove
        await using var otherTester = AppHost.NewBlazorTester(Out);
        var otherSession = otherTester.Session;
        await otherTester.SignInAsUniqueAlice();

        var otherCommander = otherTester.Commander;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => {
            await otherCommander.Call(new Media_RemoveMedia(otherSession, mediaId));
        });

        // Owner can remove
        await commander.Call(new Media_RemoveMedia(ownerSession, mediaId));

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
        var mediaId = await commander.Call(new Media_ReserveMedia(session, "delete-test"));

        // Verify both media and progress exist
        var media = await mediaBackend.GetFull(mediaId, default);
        media.Should().NotBeNull();

        var progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().NotBeNull();

        // Act: Remove media
        await commander.Call(new Media_RemoveMedia(session, mediaId));

        // Assert: Both media and progress are removed
        media = await mediaBackend.GetFull(mediaId, default);
        media.Should().BeNull();

        progress = await mediaProgressBackend.Get(mediaId, default);
        progress.Should().BeNull();
    }

    private static RpcStream<byte[]> ToRpcStream(byte[] data, int subChunkSize)
    {
        return StandardRpcStream.NewUpload(Produce());

        async IAsyncEnumerable<byte[]> Produce()
        {
            for (var pos = 0; pos < data.Length; pos += subChunkSize) {
                var size = Math.Min(subChunkSize, data.Length - pos);
                var chunk = new byte[size];
                Array.Copy(data, pos, chunk, 0, size);
                yield return chunk;
                await Task.Yield();
            }
        }
    }
}
