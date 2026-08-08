namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Opens a <see cref="GeoPoint"/> in the map app the OS picks by default, or in any specific
/// map app installed on the device. There are none in the browser, so the base implementation
/// lists nothing and opens Google Maps - which is also the fallback for an app that fails.
/// </summary>
public class ExternalMapOpener(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private static readonly Task<IReadOnlyList<MapApp>> NoMapAppsTask
        = Task.FromResult<IReadOnlyList<MapApp>>([]);

    private readonly MutableState<IReadOnlyList<MapApp>?> _apps = hub.StateFactory.NewMutable(
        (IReadOnlyList<MapApp>?)null,
        StateCategories.Get(typeof(ExternalMapOpener), nameof(_apps)));

    public async Task<IReadOnlyList<MapApp>> GetApps()
    {
        if (_apps.Value is { } apps)
            return apps;

        apps = await GetPlatformApps().ConfigureAwait(false);
        _apps.Value = apps;
        return apps;
    }

    public virtual Task Open(GeoPoint point, string? name = null)
        => Hub.ExternalUrlOpener.Open(point.ToGoogleMapsUrl());
    public virtual Task Open(MapApp mapApp, GeoPoint point, string? name = null)
        => Open(point, name);

    // Protected methods

    protected virtual Task<IReadOnlyList<MapApp>> GetPlatformApps()
        => NoMapAppsTask;
}
