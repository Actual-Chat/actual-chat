using ActualChat.UI.Blazor.App.Services;
using Android.Content;

namespace ActualChat.App.Maui.Location;

public sealed class AndroidLocationTracker : ILocationTracker, IDisposable
{
    private static volatile AndroidLocationTracker? _instance;

    private static Context Context => Platform.AppContext;

    private readonly MutableState<GeoPoint?> _lastKnown;

    public IState<GeoPoint?> LastKnown => _lastKnown;
    public bool IsTracking { get; private set; }

    public AndroidLocationTracker(AppUIHub hub)
    {
        _lastKnown = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(LastKnown)));
        Interlocked.Exchange(ref _instance, this);
    }

    public Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return Task.CompletedTask;

        IsTracking = true;
        var intent = new Intent(Context, typeof(AndroidLocationForegroundService));
        intent.SetAction(AndroidLocationForegroundService.ActionStart);
        Context.StartForegroundService(intent);
        return Task.CompletedTask;
    }

    public Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return Task.CompletedTask;

        IsTracking = false;
        _lastKnown.Value = null;
        var intent = new Intent(Context, typeof(AndroidLocationForegroundService));
        Context.StopService(intent);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Interlocked.CompareExchange(ref _instance, null, this);
        if (!IsTracking)
            return;

        IsTracking = false;
        var intent = new Intent(Context, typeof(AndroidLocationForegroundService));
        Context.StopService(intent);
    }

    // Internal methods

    internal static void ReportLocation(GeoPoint point)
    {
        if (_instance is { } instance)
            instance._lastKnown.Value = point;
    }
}
