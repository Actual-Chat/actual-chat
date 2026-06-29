using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;
using Android.Content;

namespace ActualChat.App.Maui.Location;
public sealed class AndroidLocationTracker : MauiLocationTrackerBase, IDisposable
{
    private static volatile AndroidLocationTracker? _instance;

    private static Context Context => Platform.AppContext;

    private readonly AppUIHub _hub;

    public AndroidLocationTracker(AppUIHub hub) : base(hub)
    {
        _hub = hub;
        Interlocked.Exchange(ref _instance, this);
    }

    public override async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        var settings = await _hub.LocalSettings.LocalAppSettings().Get(cancellationToken).ConfigureAwait(false);
        var intent = new Intent(Context, typeof(AndroidLocationForegroundService));
        intent.SetAction(AndroidLocationForegroundService.ActionStart);
        intent.PutExtra(AndroidLocationForegroundService.ExtraAccuracy, (int)settings.LocationAccuracyOrDefault);
        Context.StartForegroundService(intent);
    }

    public override Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return Task.CompletedTask;

        IsTracking = false;
        SetLocation(null);
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
        => _instance?.SetLocation(point);
}
