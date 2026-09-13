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
        await sessions.GetOrReserveMedia(sessionId, CancellationToken.None);

        // act
        sessions.ReleaseReference(sessionId);
        await operations.WhenIdle();

        // assert
        operations.RemovedMediaIds.Should().ContainSingle();
    }

    // Private methods

    private UploadSessions NewUploadSessions(IUploadOperations operations)
        => new (ScopedServices.GetRequiredService<AppUIHub>(), operations);

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
    // GetOrReserveMedia can complete, everything past it hangs (respecting cancellation)
    // so the session is always caught mid-flight, never Completed, once discarded.
    private sealed class FakeUploadOperations : IUploadOperations
    {
        private int _pendingCount;

        public List<MediaId> RemovedMediaIds { get; } = new();
        public VideoTranscoder VideoTranscoder { get; } = new();

        public Moment Now() => Moment.EpochStart;

        public Task<MediaId> ReserveMediaId(UploadSessionSnapshot snapshot, CancellationToken cancellationToken = default)
            => Track(() => Task.FromResult(MediaId.New(MediaId.NewScope())));

        public Task UploadData(
            UploadSource source,
            UploadSessionSnapshotAccessor snapshotAccessor,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
            => Track(() => TaskExt.NeverEnding(cancellationToken));

        public Task StartServerProcessing(UploadSessionSnapshot snapshot, CancellationToken cancellationToken = default)
            => Track(() => TaskExt.NeverEnding(cancellationToken));

        public Task<MediaRef> WaitForProcessingCompletion(
            UploadSessionSnapshot snapshot,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken1 = default)
            => Track<MediaRef>(async () => {
                await TaskExt.NeverEnding(cancellationToken1).ConfigureAwait(false);
                throw new InvalidOperationException("Not reached in tests");
            });

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
    }
}
