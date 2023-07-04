using Stl.IO;

namespace ActualChat.Kubernetes;

public sealed class KubeToken : WorkerBase
{
    private IServiceProvider Services { get; }
    private FilePath Path { get; }
    private ILogger Log { get; }

    public bool IsEmulated => Path.IsEmpty;
    public IMutableState<string> State { get; }
    public string Value => State.Value;

    public KubeToken(IServiceProvider services, FilePath path)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Path = path;

        var value = IsEmulated ? "" : File.ReadAllText(Path);
        State = services.StateFactory().NewMutable(value.Trim());
        this.Start();
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        if (IsEmulated)
            return;

        using var watcher = new FileSystemWatcher();
        watcher.Path = Path.DirectoryPath;
        watcher.Filter = Path.FileName;
        watcher.NotifyFilter = NotifyFilters.LastWrite;
        watcher.Changed += OnChanged;
        watcher.EnableRaisingEvents = true;

        await WaitForCancellation().ConfigureAwait(false);

        async Task WaitForCancellation() {
            using var dTask = cancellationToken.ToTask();
            await dTask.Resource.ConfigureAwait(false);
        }

        void OnChanged(object sender, FileSystemEventArgs e) {
            _ = BackgroundTask.Run(async () => {
                Log.LogInformation("Kubernetes token changed");
                var value = await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false);
                State.Value = value.Trim();
            }, cancellationToken);
        }
    }
}
