using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// <see cref="ILocationTracker"/> backed by MAUI Essentials <see cref="Geolocation"/>.
/// Foreground-only (no background-service integration), so it's used on platforms that
/// don't need background sharing (Windows); Android/iOS use native background-capable trackers.
/// </summary>
public sealed class MauiLocationTracker(AppUIHub hub) : MauiLocationTrackerBase(hub), IDisposable
{
    private readonly IGeolocation _geolocation = Geolocation.Default;

    public void Dispose()
    {
        if (!IsTracking)
            return;

        // Geolocation.Default is process-wide, so both the session and these handlers outlive the
        // scope this tracker belongs to - a recycled scope would leak them.
        IsTracking = false;
        DetachHandlers();
        _geolocation.StopListeningForeground();
    }

    public override async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        SetError(null);
        _geolocation.LocationChanged += OnLocationChanged;
        _geolocation.ListeningFailed += OnListeningFailed;
        var accuracy = await GetGeolocationAccuracy(cancellationToken).ConfigureAwait(false);
        var request = new GeolocationListeningRequest(accuracy, Constants.Location.UpdatePeriod);
        try {
            if (!await _geolocation.StartListeningForegroundAsync(request).ConfigureAwait(false))
                throw StandardError.External("Geolocation listening didn't start.");
        }
        catch (Exception e) {
            Log.LogError(e, "Start: failed to start geolocation listening");
            // Roll back so a later Start can retry from a clean state.
            IsTracking = false;
            SetError(ToTrackingError(e));
            DetachHandlers();
            throw;
        }
    }

    public override Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return Task.CompletedTask;

        IsTracking = false;
        SetCached(null);
        DetachHandlers();
        _geolocation.StopListeningForeground();
        return Task.CompletedTask;
    }

    // Private methods

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
        => SetCached(e.Location.ToGeoFix());

    private void OnListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
    {
        // MAUI stops the underlying session before raising this, so there's no self-heal:
        // reset tracking state and detach so a later Start re-arms from scratch.
        IsTracking = false;
        DetachHandlers();
        SetError(e.Error switch {
            GeolocationError.Unauthorized => GeoTrackingError.PermissionDenied,
            GeolocationError.PositionUnavailable => GeoTrackingError.PositionUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(e.Error), e.Error, null),
        });
    }

    private void DetachHandlers()
    {
        _geolocation.LocationChanged -= OnLocationChanged;
        _geolocation.ListeningFailed -= OnListeningFailed;
    }
}
