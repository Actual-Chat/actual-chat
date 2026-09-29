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

        Log.LogInformation("EnqueueOrNavigateToUrl, Url: {Url}", url);
        EnqueueOrRun(
            nameof(EnqueueOrNavigateToUrl),
            c => c.GetRequiredService<AutoNavigationUI>().DispatchNavigateTo(url, reason));
    }

    // Runs on the Blazor dispatcher of the current scope; a task queued before the scope exists
    // runs while its first render is being prepared, ahead of anything that waits for that render
    public static void EnqueueOrRun(string name, Func<IServiceProvider, Task> taskFactory)
    {
        lock (Queue) {
            if (ScopedServices is { } c) {
                // Run right now
                Dispatcher dispatcher;
                try {
                    dispatcher = c.GetRequiredService<Dispatcher>();
                }
                catch (ObjectDisposedException e) {
                    Log.LogWarning(e, "{Name}: ScopedServices is disposed -> ignore", name);
                    Reset();
                    return;
                }
                _ = dispatcher.InvokeAsync(() => Run(c, taskFactory));
                return;
            }

            // Enqueue
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
