using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Services;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class UploadActivitySourceTest : TestBase
{
    private const long FileLength = 68_000_000;
    private IServiceProvider ScopedServices { get; }

    public UploadActivitySourceTest(ITestOutputHelper @out) : base(@out)
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
            .AddSingleton(_ => new FileUploader([]))
            .AddScoped<UIHub>()
            .AddScoped<AppUIHub>()
            .AddScoped<IUploadSessionRepo, TestUploadSessionRepo>()
            .AddScoped(c => new UploadSessions(c.GetRequiredService<AppUIHub>()))
            .AddFusion(fusion => {
                fusion.AddBlazor();
                fusion.AddService<UploadSessionsState>(ServiceLifetime.Scoped);
                fusion.AddService<UploadActivitySource>(ServiceLifetime.Scoped);
            })
            .BuildServiceProvider();
        ScopedServices = services.CreateScope().ServiceProvider;
    }

    [Fact]
    public async Task ReportsBytesMatchingTheStagePercent()
    {
        // arrange
        var sessions = ScopedServices.GetRequiredService<UploadSessions>();
        var sessionsState = ScopedServices.GetRequiredService<UploadSessionsState>();
        var fileProvider = new TestFileProvider("video.mp4", FileLength);
        var sessionId = await sessions.CreateSession(fileProvider, new MetadataBag(), "");
        sessionsState.SetProgress(sessionId, new UploadSessionProgress(UploadStage.Uploading, 45) {
            TotalBytes = FileLength,
        });

        // act
        var source = ScopedServices.GetRequiredService<UploadActivitySource>();
        var cActivity = await Computed.Capture(() => source.GetActivity(CancellationToken.None));
        cActivity = await cActivity.When(x => x is not null).WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        var upload = cActivity.Value.Should().BeOfType<UploadActivity>().Subject;
        upload.FileCount.Should().Be(1);
        upload.TotalBytes.Should().Be(FileLength);
        upload.Progress.Should().BeApproximately(0.45, 0.001);
        upload.BytesUploaded.Should().BeCloseTo((long)(FileLength * 0.45), 1);
        upload.Items.Single().FileName.Should().Be("video.mp4");
    }

    // Nested types

    private sealed class TestFileProvider(string fileName, long length) : IFileProvider
    {
        public FileMetadata Metadata { get; } = new() {
            FileName = fileName,
            FileType = "video/mp4",
            Length = length,
        };
        public Task PrepareForSaving() => Task.CompletedTask;
        public void Initialize(IServiceProvider services) { }
        public Task<bool> CheckAccess() => ActualLab.Async.TaskExt.TrueTask;
        public Task<bool> WhenUserConsentGranted() => ActualLab.Async.TaskExt.TrueTask;
        public Task ClearForRemoving() => Task.CompletedTask;
        public Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WhenFileStreamReady() => Task.CompletedTask;
        public UploadSource GetUploadSource() => throw new NotSupportedException();
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
}
