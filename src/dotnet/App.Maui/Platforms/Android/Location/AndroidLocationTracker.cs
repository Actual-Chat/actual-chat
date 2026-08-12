using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Location.AndroidLocationForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Location;
public sealed class AndroidLocationTracker : MauiLocationTrackerBase, IDisposable
{
    private static volatile AndroidLocationTracker? _instance;

    private static Context Context => Platform.AppContext;

    public AndroidLocationTracker(AppUIHub hub) : base(hub)
        => Interlocked.Exchange(ref _instance, this);

    public void Dispose()
    {
        Interlocked.CompareExchange(ref _instance, null, this);
        if (!IsTracking)
            return;

        IsTracking = false;
        StopService();
    }

    public override async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        SetError(null);
        try {
            var accuracy = await GetAccuracy(cancellationToken).ConfigureAwait(false);
            var intent = new Intent(Context, typeof(AndroidLocationForegroundService));
            intent.SetAction(AndroidLocationForegroundService.ActionStart);
            intent.PutExtra(IntentExtras.Accuracy, (int)accuracy);
            Context.StartForegroundService(intent);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Failed to start Android location foreground service");
            IsTracking = false;
            SetError(ToTrackingError(e));
            throw;
        }
    }

    public override Task Stop(CancellationToken cancellationToken)
    {
        if (IsTracking) {
            IsTracking = false;
            SetCached(null);
        }
        // Unconditional: the service outlives the process that started it, so this instance may have
        // to stop one it never started - and this is the only path that runs when sharing ends.
        StopService();
        return Task.CompletedTask;
    }

    // Protected/internal methods

    internal static void ReportLocation(GeoFix fix)
        => _instance?.SetCached(fix);

    internal static void ReportError(GeoTrackingError error)
        => _instance?.SetError(error);

    // Private methods

    private static void StopService()
        => Context.StopService(new Intent(Context, typeof(AndroidLocationForegroundService)));
}
