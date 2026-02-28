using ActualChat.Media;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class UploadSessionFlowTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ShouldUploadTextFile()
    {
        // Arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();

        var hub = tester.ScopedAppServices.AppUIHub();
        var mediaBackend = tester.AppServices.GetRequiredService<IMediaBackend>();
        var mediaProgressBackend = tester.AppServices.GetRequiredService<IMediaProgressBackend>();

        var testContent = "Hello, this is test content for UploadSession!"u8.ToArray();
        var fileProvider = new DataFileProvider(testContent, "test-upload.txt", "text/plain");
        fileProvider.Initialize(hub.Services);
        var metadata = new PropertyBag().Set("TestKey", "TestValue");
        var scope = "upload-session-test";

        var uploadOperations = new UploadOperations(hub);
        var snapshot = UploadSession.NewUploadSnapshot(fileProvider, metadata, uploadOperations.Now(), scope);
        var uploadSession = new UploadSession(snapshot, uploadOperations, storage: null);

        // Act 1: Start upload
        var started = uploadSession.Resume();
        started.Should().BeTrue();

        // Act 2: Wait for media to be reserved
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var mediaId = await uploadSession.WhenMediaReserved.WaitAsync(cts.Token);

        mediaId.Should().NotBeNull();
        mediaId.Scope.Should().Be(scope);

        // Act 3: Wait for upload to complete
        while (!uploadSession.IsCompleted && !uploadSession.IsFailed)
            await Task.Delay(100, cts.Token);

        // Assert: Upload completed successfully
        uploadSession.IsFailed.Should().BeFalse(uploadSession.LastError?.ToString() ?? "no error");
        uploadSession.IsCompleted.Should().BeTrue();
        uploadSession.MediaContent.Should().NotBeNull();
        uploadSession.MediaContent!.BlobId.Should().NotBeNullOrEmpty();

        // Verify media exists on server with Ready stage (may need to wait for server processing)
        await TestExt.When(async () => {
            var progress = await mediaProgressBackend.Get(mediaId, default);
            progress.Should().NotBeNull();
            progress.Stage.Should().Be(MediaStage.Ready);
        }, TimeSpan.FromSeconds(10));

        var media = await mediaBackend.GetFull(mediaId, default);
        media.Should().NotBeNull();
        media.BlobId.Should().Be(uploadSession.MediaContent.BlobId);
    }

    // Test file provider that uploads data via ChunkedFileUploader
    private sealed class DataFileProvider(byte[] data, string fileName, string contentType) : IFileProvider
    {
        public FileMetadata Metadata { get; } = new() {
            FileName = fileName,
            FileType = contentType,
            Length = data.Length,
        };

        public void Initialize(IServiceProvider services) {}

        public Task PrepareForSaving() => Task.CompletedTask;

        public Task<bool> CheckAccess() => Task.FromResult(true);

        public Task<bool> WhenUserConsentGranted() => Task.FromResult(true);

        public Task ClearForRemoving() => Task.CompletedTask;

        public Task<string> GetPreviewUrl() => Task.FromResult("");

        public Task WhenFileStreamReady() => Task.CompletedTask;

        public UploadSource GetUploadSource()
        {
            var metadata = new UploadSourceMetadata(
                Metadata.FileType,
                Metadata.Length,
                Metadata.FileName);
            return new UploadSource(metadata, new StreamUploadSource(GetFile));

            Task<Stream> GetFile()
                => Task.FromResult<Stream>(new MemoryStream(data));
        }
    }
}
