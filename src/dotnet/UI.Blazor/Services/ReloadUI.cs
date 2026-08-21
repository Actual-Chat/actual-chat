using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class ReloadUI(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public virtual void Reload(bool clearLocalSettings = false)
    {
        Log.LogInformation("Reload requested");
        var circuitHub = Services.GetRequiredService<CircuitHub>();
        _ = circuitHub.WhenInitialized.ContinueWith(_ => circuitHub.Dispatcher.InvokeAsync(async () => {
            Log.LogInformation("Reloading...");
            try {
                var hostInfo = Services.HostInfo();
                var nav = Services.GetRequiredService<NavigationManager>();
                await Clear(clearLocalSettings).ConfigureAwait(true); // Nav requires UI context
                if (hostInfo.HostKind.IsApp())
                    AppNavigationQueue.Reset();
                nav.NavigateTo(Links.Home, true);
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed");
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public virtual Task ReplaceSession(CancellationToken cancellationToken)
    {
        // AuthHelper drops an invalid cookie session and issues a new one on the next request
        Reload(clearLocalSettings: true);
        return Task.CompletedTask;
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
}
