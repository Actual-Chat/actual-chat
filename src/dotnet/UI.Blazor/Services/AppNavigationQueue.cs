namespace ActualChat.UI.Blazor.Services;

/// This type should be used only in apps, i.e. not in SSB.
public static class AppNavigationQueue
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For(typeof(AppNavigationQueue));
    private static readonly List<Func<IServiceProvider, Task>> Queue = new();

    public static IServiceProvider? ScopedServices { get; private set; }

    public static void Reset()
    {
        Log.LogDebug("Reset");
        lock (Queue) {
            ScopedServices = null;
            Queue.Clear();
        }
    }

    public static void EnqueueOrNavigateToNotificationUrl(string? url)
    {
        if (url.IsNullOrEmpty()) {
            Log.LogWarning("EnqueueOrNavigateToNotificationUrl: empty url -> ignore");
            return;
        }

        Func<IServiceProvider, Task> taskFactory = c => {
            var notificationUI = c.GetRequiredService<INotificationUI>();
            return notificationUI.NavigateToNotificationUrl(url);
        };

        Log.LogInformation("EnqueueOrNavigateToNotificationUrl, Url: {Url}", url);
        lock (Queue) {
            if (ScopedServices is { } c) {
                // Navigate right now
                var dispatcher = c.GetRequiredService<Dispatcher>();
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
            ScopedServices = scopedServices;
            var tasks = Queue.Select(taskFactory => Run(scopedServices, taskFactory)).ToList();
            Queue.Clear();
            return tasks;
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
}
