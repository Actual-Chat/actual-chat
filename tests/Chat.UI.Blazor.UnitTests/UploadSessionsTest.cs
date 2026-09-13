using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Services;
using Microsoft.Extensions.Hosting;
using AsyncTaskExt = ActualLab.Async.TaskExt;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class UploadSessionsTest : TestBase
{
    private IServiceProvider ScopedServices { get; }

    public UploadSessionsTest(ITestOutputHelper @out) : base(@out)
    {
        var hostInfo = new HostInfo {
            HostKind = HostKind.MauiApp,
            AppKind = AppKind.Android,
            Environment = Environments.Development,
            BaseUrl = $"https://{Constants.Hosts.LocalVoxt}",
            IsTested = true,
        };
        var services = new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(_ => hostInfo)
            .AddSingleton(c => new Features(c))
            .AddSingleton(_ => new UrlMapper(hostInfo))
            .AddScoped<UIHub>()
            .AddScoped<AppUIHub>()
            .AddScoped<IUploadSessionRepo, TestUploadSessionRepo>()
            .AddFusion(fusion => {
                fusion.AddBlazor();
                fusion.AddService<UploadSessionsState>(ServiceLifetime.Scoped);
            })
            .BuildServiceProvider();
        ScopedServices = services.CreateScope().ServiceProvider;
    }

    [Fact]
    public async Task DiscardingASessionShouldRemoveItsReservedMedia()
    {
        // arrange
        var operations = new FakeUploadOperations();
        var sessions = NewUploadSessions(operations);
        var sessionId = await sessions.CreateSession(new TestFileProvider(), MetadataBag.Empty, "");
        sessions.AddReference(sessionId);
        sessions.Resume(sessionId);
        var mediaId = await sessions.GetOrReserveMedia(sessionId, CancellationToken.None);

        // act
        sessions.ReleaseReference(sessionId);
        await operations.WhenIdle();

        // assert
        operations.RemovedMediaIds.Should().ContainSingle().Which.Should().Be(mediaId);
    }

    [Fact]
    public async Task MediaBoundSessionShouldKeepItsMedia()
    {
        // arrange
        var operations = new FakeUploadOperations(completesUpload: true);
        var sessions = NewUploadSessions(operations);
        var sessionId = await sessions.CreateSession(new TestFileProvider(), MetadataBag.Empty, "");
        sessions.AddReference(sessionId, isMediaBound: true);
        sessions.Resume(sessionId);
        var session = await sessions.TryGetSession(sessionId);
        await WaitUntilCompleted(session!);

        // act
        sessions.ReleaseReference(sessionId);
        await operations.WhenIdle();

        // assert
        operations.RemovedMediaIds.Should().BeEmpty(
            "media a posted message references is not an orphan, whichever reference is released last");
    }

    [Fact]
    public async Task CompletedButUnboundSessionShouldRemoveItsMedia()
    {
        // arrange - what a preset change after the upload already finished discards
        var operations = new FakeUploadOperations(completesUpload: true);
        var sessions = NewUploadSessions(operations);
        var sessionId = await sessions.CreateSession(new TestFileProvider(), MetadataBag.Empty, "");
        sessions.AddReference(sessionId);
        sessions.Resume(sessionId);
        var session = await sessions.TryGetSession(sessionId);
        await WaitUntilCompleted(session!);
        var mediaId = session!.MediaId!;

        // act
        sessions.ReleaseReference(sessionId);
        await operations.WhenIdle();

        // assert
        operations.RemovedMediaIds.Should().ContainSingle().Which.Should().Be(mediaId);
    }

    [Fact]
    public void ReservedMediaShouldBeRequestedAsAChatEntryAttachment()
    {
        // arrange
        var snapshot = UploadSession.NewUploadSnapshot(
            new TestFileProvider(), MetadataBag.Empty, Moment.EpochStart, "");

        // act
        var command = UploadOperations.CreateReserveMediaCommand(Session.New(), snapshot);

        // assert
        command.Kind.Should().Be(MediaKind.ChatEntryAttachment,
            "a media row reserved without it falls back to the legacy 1920px image processor");
    }

    [Fact]
    public async Task StaleSessionShouldRemoveItsReservedMedia()
    {
        // arrange
        var operations = new FakeUploadOperations();
        var sessions = NewUploadSessions(operations);
        var mediaId = MediaId.New(MediaId.NewScope());
        var snapshot = await SaveStaleSnapshot(UploadSessionState.Uploading, mediaId);

        // act - no in-memory session exists for this id, so this hits the crash-recovery branch
        await sessions.DeleteStaleSession(snapshot.SessionId);

        // assert
        operations.RemovedMediaIds.Should().ContainSingle().Which.Should().Be(mediaId);
    }

    [Fact]
    public async Task StaleCompletedSessionShouldKeepItsMedia()
    {
        // arrange
        var operations = new FakeUploadOperations();
        var sessions = NewUploadSessions(operations);
        var mediaId = MediaId.New(MediaId.NewScope());
        var snapshot = await SaveStaleSnapshot(UploadSessionState.Completed, mediaId);

        // act
        await sessions.DeleteStaleSession(snapshot.SessionId);

        // assert
        operations.RemovedMediaIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ResumingAnInitializingSnapshotWithReservedMediaShouldStillComplete()
    {
        // arrange - simulates a crash between reserving the media id and persisting
        // the ClientProcessing transition: Initializing on disk, but ReservedMediaId is set
        var operations = new FakeUploadOperations(completesUpload: true);
        var sessions = NewUploadSessions(operations);
        var mediaId = MediaId.New(MediaId.NewScope());
        var snapshot = await SaveStaleSnapshot(UploadSessionState.Initializing, mediaId);

        // act
        var session = await sessions.TryGetSession(snapshot.SessionId);
        sessions.Resume(snapshot.SessionId);
        await WaitUntilTerminatedOrFailed(session!);

        // assert
        session!.IsCompleted.Should().BeTrue(session!.LastError?.ToString() ?? "no error");
        operations.ReserveMediaIdCallCount.Should().Be(0);
    }

    // Private methods

    private UploadSessions NewUploadSessions(IUploadOperations operations)
        => new (ScopedServices.GetRequiredService<AppUIHub>(), operations);

    private async Task<UploadSessionSnapshot> SaveStaleSnapshot(UploadSessionState state, MediaId mediaId)
    {
        var snapshot = UploadSession.NewUploadSnapshot(
            new TestFileProvider(), MetadataBag.Empty, Moment.EpochStart, "");
        snapshot = snapshot with { CurrentState = state, ReservedMediaId = mediaId };
        await ScopedServices.GetRequiredService<IUploadSessionRepo>().Save(snapshot);
        return snapshot;
    }

    private static async Task WaitUntilCompleted(UploadSession session)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!session.IsCompleted)
            await Task.Delay(10, cts.Token).ConfigureAwait(false);
    }

    private static async Task WaitUntilTerminatedOrFailed(UploadSession session)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!session.IsCompleted && !session.IsFailed)
            await Task.Delay(10, cts.Token).ConfigureAwait(false);
    }

    // Nested types

    private sealed class TestFileProvider : IFileProvider
    {
        public FileMetadata Metadata { get; } = new() {
            FileName = "test.txt",
            FileType = "text/plain",
            Length = 16,
        };
        public Task PrepareForSaving() => Task.CompletedTask;
        public void Initialize(IServiceProvider services) { }
        public Task<bool> CheckAccess() => AsyncTaskExt.TrueTask;
        public Task<bool> WhenUserConsentGranted() => AsyncTaskExt.TrueTask;
        public Task ClearForRemoving() => Task.CompletedTask;
        public Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WhenFileStreamReady() => Task.CompletedTask;

        public UploadSource GetUploadSource()
        {
            var metadata = new UploadSourceMetadata(Metadata.FileType, Metadata.Length, Metadata.FileName);
            return new UploadSource(metadata, new StreamUploadSource(GetStream));

            Task<Stream> GetStream() => Task.FromResult<Stream>(new MemoryStream(new byte[16]));
        }
    }

    private sealed class TestUploadSessionRepo : IUploadSessionRepo
    {
        private readonly ConcurrentDictionary<string, UploadSessionSnapshot> _snapshots = new();
        public Task Save(UploadSessionSnapshot session, bool flush = true)
        {
            _snapshots[session.SessionId] = session;
            return Task.CompletedTask;
        }
        public Task<UploadSessionSnapshot?> Get(string sessionId)
            => Task.FromResult(_snapshots.GetValueOrDefault(sessionId));
        public Task<IEnumerable<KeyValuePair<string, UploadSessionSnapshot>>> GetAll()
            => Task.FromResult<IEnumerable<KeyValuePair<string, UploadSessionSnapshot>>>(_snapshots.ToArray());
        public Task Delete(string sessionId)
        {
            _snapshots.TryRemove(sessionId, out _);
            return Task.CompletedTask;
        }
        public Task Flush() => Task.CompletedTask;
    }

    // A minimal IUploadOperations double: ReserveMediaId resolves right away so
    // GetOrReserveMedia can complete. By default everything past it hangs (respecting
    // cancellation) so the session is always caught mid-flight, never Completed, once
    // discarded; completesUpload: true instead lets the whole pipeline run to Completed,
    // for the "must not touch a bound media" test.
    private sealed class FakeUploadOperations(bool completesUpload = false) : IUploadOperations
    {
        private int _pendingCount;

        public List<MediaId> RemovedMediaIds { get; } = new();
        public VideoTranscoder VideoTranscoder { get; } = new();
        public int ReserveMediaIdCallCount;

        public Moment Now() => Moment.EpochStart;

        public Task<MediaId> ReserveMediaId(
            UploadSessionSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ReserveMediaIdCallCount);
            return Track(() => Task.FromResult(MediaId.New(MediaId.NewScope())));
        }

        public Task UploadData(
            UploadSource source,
            UploadSessionSnapshotAccessor snapshotAccessor,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
            => Track(() => completesUpload ? Task.CompletedTask : TaskExt.NeverEnding(cancellationToken));

        public Task StartServerProcessing(UploadSessionSnapshot snapshot, CancellationToken cancellationToken = default)
            => Track(() => completesUpload ? Task.CompletedTask : TaskExt.NeverEnding(cancellationToken));

        public Task<MediaRef> WaitForProcessingCompletion(
            UploadSessionSnapshot snapshot,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
            => Track(() => completesUpload
                ? Task.FromResult(new MediaRef(snapshot.ReservedMediaId!, "fake-blob"))
                : WaitForever(cancellationToken));

        public Task RemoveUpload(UploadId uploadId, CancellationToken cancellationToken)
            => Track(() => Task.CompletedTask);

        public Task RemoveMedia(MediaId mediaId, CancellationToken cancellationToken)
            => Track(() => {
                RemovedMediaIds.Add(mediaId);
                return Task.CompletedTask;
            });

        public async Task WhenIdle()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var idleStreak = 0;
            while (idleStreak < 5) {
                idleStreak = Volatile.Read(ref _pendingCount) == 0 ? idleStreak + 1 : 0;
                await Task.Delay(10, cts.Token).ConfigureAwait(false);
            }
        }

        private async Task Track(Func<Task> operation)
        {
            Interlocked.Increment(ref _pendingCount);
            try {
                await operation().ConfigureAwait(false);
            }
            finally {
                Interlocked.Decrement(ref _pendingCount);
            }
        }

        private async Task<T> Track<T>(Func<Task<T>> operation)
        {
            Interlocked.Increment(ref _pendingCount);
            try {
                return await operation().ConfigureAwait(false);
            }
            finally {
                Interlocked.Decrement(ref _pendingCount);
            }
        }

        private static async Task<MediaRef> WaitForever(CancellationToken cancellationToken)
        {
            await TaskExt.NeverEnding(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Not reached in tests");
        }
    }
}
