namespace ActualChat.UI.Blazor.Services;

/// This type should be used only in apps, i.e. not in SSB.
public static class AppNavigationQueue
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For(typeof(AppNavigationQueue));
    private static readonly List<Func<IServiceProvider, Task>> Queue = new();
    private static volatile ContainerInfo? _containerInfo;

    private static IServiceProvider? ScopedServices {
        get {
            if (_containerInfo is null)
                return null;

            if (!_containerInfo.DisposalTracker.IsDisposed)
                return _containerInfo.Services;

            Log.LogWarning("Attempt to access disposed ScopedServices. Disposal stack trace: {DisposalStackTrace}", _containerInfo.DisposalTracker.DisposalStackTrace);
            Reset();
            return null;
        }
    }

    public static void Reset()
    {
        Log.LogDebug("Reset");
        lock (Queue) {
            _containerInfo = null;
            Queue.Clear();
        }
    }

    public static void EnqueueOrNavigateToUrl(string? url, AutoNavigationReason reason)
    {
        if (url.IsNullOrEmpty()) {
            Log.LogWarning("EnqueueOrNavigateToUrl: empty url -> ignore");
            return;
        }

        Func<IServiceProvider, Task> taskFactory = c => {
            var autoNavigationUI = c.GetRequiredService<AutoNavigationUI>();
            return autoNavigationUI.DispatchNavigateTo(url, reason);
        };

        Log.LogInformation("EnqueueOrNavigateToUrl, Url: {Url}", url);
        lock (Queue) {
            if (ScopedServices is { } c) {
                // Navigate right now
                Dispatcher dispatcher;
                try {
                    dispatcher = c.GetRequiredService<Dispatcher>();
                }
                catch (ObjectDisposedException e) {
                    Log.LogWarning(e, "EnqueueOrNavigateToUrl: ScopedServices is disposed -> ignore");
                    Reset();
                    return;
                }
                _ = dispatcher.InvokeAsync(() => Run(c, taskFactory));
                return;
            }

            // Enqueue navigation
            Queue.Add(taskFactory);
        }
    }

    public static IReadOnlyList<Task> DequeueAll(IServiceProvider scopedServices)
    {
        Log.LogDebug("DequeueAll");
        lock (Queue) {
            try {
                var tracker = scopedServices.GetRequiredService<ContainerDisposalTracker>();
                _containerInfo = new ContainerInfo(scopedServices, tracker);
                var tasks = Queue.Select(taskFactory => Run(scopedServices, taskFactory)).ToList();
                Queue.Clear();
                return tasks;
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to get ContainerDisposalTracker instance");
                Queue.Clear();
                return Array.Empty<Task>();
            }
        }
    }

    // Private methods

    private static async Task Run(IServiceProvider scopedServices, Func<IServiceProvider, Task> taskFactory)
    {
        try {
            await taskFactory.Invoke(scopedServices).ConfigureAwait(false);
        }
        catch (Exception e) {
            var log = scopedServices.LogFor(typeof(AppNavigationQueue));
            log.LogError(e, "Enqueued task failed");
        }
    }

    // Nested types
    public class ContainerDisposalTracker : IDisposable
    {
        private bool _disposed;
        private string _disposalStackTrace = "";

        public bool IsDisposed => _disposed;
        public string DisposalStackTrace => _disposalStackTrace;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _disposalStackTrace = Environment.StackTrace;
        }
    }

    private record ContainerInfo(IServiceProvider Services, ContainerDisposalTracker DisposalTracker);
}
