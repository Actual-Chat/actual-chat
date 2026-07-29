using ActualChat.Media;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Services;

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
        var metadata = new MetadataBag().Set("TestKey", "TestValue");
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
        uploadSession.MediaRef.Should().NotBeNull();
        uploadSession.MediaRef!.BlobId.Should().NotBeNullOrEmpty();

        // Verify media exists on server with Ready stage (may need to wait for server processing)
        await TestExt.When(async () => {
            var progress = await mediaProgressBackend.Get(mediaId, default);
            progress.Should().NotBeNull();
            progress.Stage.Should().Be(MediaProcessingStage.Ready);
        }, TimeSpan.FromSeconds(10));

        var media = await mediaBackend.GetFull(mediaId, default);
        media.Should().NotBeNull();
        media.BlobId.Should().Be(uploadSession.MediaRef.BlobId);
    }

    [Fact]
    public async Task ShouldRestoreUploadSessionFromRepo()
    {
        await using var appHost = await NewAppHost("restore-session", options => options with {
            ConfigureServices = (_, services) => {
                services.AddScoped<IUploadSessionRepo, InMemoryUploadSessionRepo>();
            },
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();

        var hub = tester.ScopedAppServices.AppUIHub();
        var repo = tester.ScopedAppServices.GetRequiredService<IUploadSessionRepo>();
        var uploadSessions = hub.UploadSessions;

        var testContent = "Test content for restore"u8.ToArray();
        var fileProvider = new DataFileProvider(testContent, "restore-test.txt", "text/plain");
        var metadata = new MetadataBag().Set("TestKey", "TestValue");

        var uploadOperations = new UploadOperations(hub);
        var snapshot = UploadSession.NewUploadSnapshot(fileProvider, metadata, uploadOperations.Now(), "restore-test");

        // Save snapshot directly to repo (bypasses _sessions dictionary)
        await repo.Save(snapshot);

        // Act: TryGetSession should load from repo
        var session = await uploadSessions.TryGetSession(snapshot.SessionId);

        // Assert
        session.Should().NotBeNull();
        session.SessionId.Should().Be(snapshot.SessionId);
        session.CurrentState.Should().Be(snapshot.CurrentState);
        session.FileName.Should().Be("restore-test.txt");
    }

    [Fact]
    public async Task ShouldDeleteStaleUploadSession()
    {
        await using var appHost = await NewAppHost("delete-stale-session", options => options with {
            ConfigureServices = (_, services) => {
                services.AddScoped<IUploadSessionRepo, InMemoryUploadSessionRepo>();
            },
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();

        var hub = tester.ScopedAppServices.AppUIHub();
        var repo = tester.ScopedAppServices.GetRequiredService<IUploadSessionRepo>();
        var uploadSessions = hub.UploadSessions;

        var testContent = "Test content for stale delete"u8.ToArray();
        var fileProvider = new DataFileProvider(testContent, "stale-test.txt", "text/plain");
        var metadata = new MetadataBag().Set("TestKey", "TestValue");

        var uploadOperations = new UploadOperations(hub);
        var snapshot = UploadSession.NewUploadSnapshot(fileProvider, metadata, uploadOperations.Now(), "stale-test");

        // Save snapshot directly to repo (bypasses _sessions dictionary — simulates stale session)
        await repo.Save(snapshot);

        // Act: DeleteSession should handle a session not in _sessions
        await uploadSessions.DeleteStaleSession(snapshot.SessionId);

        // Assert: session is deleted from repo
        var repoSession = await repo.Get(snapshot.SessionId);
        repoSession.Should().BeNull();

        // Assert: TryGetSession also returns null
        var session = await uploadSessions.TryGetSession(snapshot.SessionId);
        session.Should().BeNull();
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

        public Task<FilePreview> GetPreview(CancellationToken cancellationToken = default) => Task.FromResult(new FilePreview(""));

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
