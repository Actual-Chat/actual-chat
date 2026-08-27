using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class ReloadUI(IServiceProvider services)
{
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private bool _isReloaded;

    protected IServiceProvider Services { get; } = services;
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public void Reload(bool clearLocalSettings = false)
    {
        Log.LogInformation("Reload requested");
        _ = Dispatch(() => ReloadOnce(clearLocalSettings));
    }

    // invalidSession is the one the caller saw as unusable, or null when it couldn't tell which that
    // was; false means it's no longer the current session, i.e. the caller acted on a stale answer.
    public virtual Task<bool> ReplaceSession(Session? invalidSession, CancellationToken cancellationToken)
    {
        // AuthHelper drops an invalid cookie session and issues a new one on the next request
        Reload(clearLocalSettings: true);
        return Task.FromResult(true);
    }

    public Task Clear(bool clearLocalSettings)
        => clearLocalSettings ? ClearLocalSettings() : Task.CompletedTask;

    public virtual async Task ClearLocalSettings()
    {
        Log.LogWarning("Cleaning local settings...");
        try {
            var localSettings = Services.GetRequiredService<LocalSettings>();
            await localSettings.Clear().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "ClearLocalSettings failed");
        }
    }

    public virtual void Quit()
        => throw new NotSupportedException("Can't close web app.");

    // Protected methods

    protected virtual Task Dispatch(Func<Task> action)
    {
        var circuitHub = Services.GetRequiredService<CircuitHub>();
        return circuitHub.WhenInitialized.ContinueWith(_ => circuitHub.Dispatcher.InvokeAsync(action),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    protected virtual async Task ForceReload()
    {
        var history = Services.GetRequiredService<History>();
        if (Services.HostInfo().HostKind.IsApp())
            AppNavigationQueue.Reset();
        await history.ForceReload(nameof(Reload), history.Uri).ConfigureAwait(true);
    }

    protected virtual void OnReloadFailed(Exception error)
        => Log.LogError(error, "Reload failed");

    // Private methods

    private async Task ReloadOnce(bool clearLocalSettings)
    {
        await _reloadLock.WaitAsync().ConfigureAwait(true);
        try {
            if (_isReloaded)
                return;

            Log.LogInformation("Reloading...");
            await Clear(clearLocalSettings).ConfigureAwait(true);
            await ForceReload().ConfigureAwait(true);
            _isReloaded = true;
        }
        catch (Exception e) {
            OnReloadFailed(e);
        }
        finally {
            _reloadLock.Release();
        }
    }
}
