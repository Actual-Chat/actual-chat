using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using AndroidUri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public sealed class AndroidMapOpener(AppUIHub hub) : MauiMapOpener(hub)
{
    private const string GoogleMapsPackageName = "com.google.android.apps.maps";
    private const int IconSize = 96;

    // The list is curated: querying the geo: intent also matches apps that aren't maps (Zoom claims it)
    // An empty UrlFormat means the app handles the shared geo: URL
    // UrlFormat arguments: {0} latitude, {1} longitude, {2} escaped label, {3} this app's package name
    // Every package must also be in AndroidManifest.xml's <queries>, or the package manager hides it
    private static readonly MapApp[] KnownMapApps = [
        NewMapApp(GoogleMapsPackageName),
        NewMapApp("com.waze", "waze://?ll={0},{1}"),
        NewMapApp("com.here.app.maps"),
        NewMapApp("com.sygic.aura", "com.sygic.aura://coordinate|{1}|{0}|show"),
        NewMapApp("com.tomtom.gplay.navapp"),
        NewMapApp("com.generalmagic.magicearth", "magicearth://?show_on_map&lat={0}&lon={1}&name={2}"),
        NewMapApp("app.organicmaps", "om://map?v=1&ll={0},{1}&n={2}"),
        NewMapApp("com.mapswithme.maps.pro", "mapsme://map?v=1&ll={0},{1}&n={2}"),
        NewMapApp("net.osmand"),
        NewMapApp("net.osmand.plus"),
        NewMapApp("com.moovit.app", "moovit://directions?dest_lat={0}&dest_lon={1}&dest_name={2}"),
        NewMapApp("com.citymapper.app.release", "citymapper://directions?endcoord={0},{1}&endname={2}"),
        NewMapApp("ru.yandex.yandexmaps", "yandexmaps://maps.yandex.ru/?pt={1},{0}&z=16"),
        NewMapApp("ru.yandex.yandexnavi", "yandexnavi://show_point_on_map?lat={0}&lon={1}&zoom=16"),
        NewMapApp("ru.dublgis.dgismobile", "dgis://2gis.ru/geo/{1},{0}"),
        // The dev/coord_type flags below say "these are raw GPS coordinates", so the app applies
        // the GCJ-02/BD-09 shift itself - unflagged WGS84 lands a few hundred meters off in China
        NewMapApp("com.autonavi.minimap",
            "androidamap://viewMap?sourceApplication={3}&poiname={2}&lat={0}&lon={1}&dev=1"),
        NewMapApp("com.baidu.BaiduMap",
            "baidumap://map/marker?location={0},{1}&title={2}&coord_type=wgs84&src={3}"),
        NewMapApp("com.tencent.map",
            "qqmap://map/marker?marker=coord:{0},{1};title:{2}&coord_type=1&referer={3}"),
        NewMapApp("com.nhn.android.nmap", "nmap://place?lat={0}&lng={1}&name={2}&appname={3}"),
        NewMapApp("net.daum.android.map", "kakaomap://look?p={0},{1}"),
        NewMapApp("com.ubercab",
            "uber://?action=setPickup&pickup=my_location"
            + "&dropoff[latitude]={0}&dropoff[longitude]={1}&dropoff[nickname]={2}"),
    ];

    protected override Task<IReadOnlyList<MapApp>> GetPlatformApps()
    {
        // Labels and icons come from the other apps' APKs, and every missing package throws -
        // way too slow for the UI thread this runs on, so the whole scan goes to the background
        var packageManager = Platform.AppContext.PackageManager;
        if (packageManager is null)
            return Task.FromResult<IReadOnlyList<MapApp>>([]);

        return BackgroundTask.Run(() => {
            var mapApps = KnownMapApps.Select(TryGetAppInfo)
                .SkipNullItems()
                .ToList();
            return Task.FromResult<IReadOnlyList<MapApp>>(mapApps);
        });

        MapApp? TryGetAppInfo(MapApp mapApp)
        {
            try {
                var applicationInfo = packageManager.GetApplicationInfo(mapApp.Key, 0);
                var title = packageManager.GetApplicationLabel(applicationInfo);
                return mapApp with {
                    Title = title.NullIfEmpty() ?? mapApp.Key,
                    IconUrl = ToIconUrl(packageManager.GetApplicationIcon(applicationInfo)),
                };
            }
            catch (PackageManager.NameNotFoundException) {
                return null;
            }
        }
    }

    protected override Task<bool> TryOpen(MapApp mapApp, GeoPoint point, string? name)
    {
        var coordinates = point.ToCoordinatesText();
        string url;
        if (mapApp.UrlFormat.IsNullOrEmpty()) {
            var label = name.NullIfEmpty() is { } n ? $"({Uri.EscapeDataString(n)})" : "";
            url = $"geo:{coordinates}?q={coordinates}{label}";
        }
        else {
            var label = Uri.EscapeDataString(name.NullIfEmpty() ?? coordinates);
            url = string.Format(mapApp.UrlFormat,
                point.LatitudeText(),
                point.LongitudeText(),
                label,
                AppInfo.Current.PackageName);
        }

        var intent = new Intent(Intent.ActionView, AndroidUri.Parse(url));
        intent.SetPackage(mapApp.Key);
        intent.SetFlags(ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(intent);
        return ActualLab.Async.TaskExt.TrueTask;
    }

    // Private methods

    private static MapApp NewMapApp(string packageName, string urlFormat = "")
        => new(packageName, "", "", urlFormat);

    private static string ToIconUrl(Drawable? icon)
    {
        if (icon is null)
            return "";

        // Adaptive icons render as a full square here - the UI rounds their corners
        using var bitmap = Bitmap.CreateBitmap(IconSize, IconSize, Bitmap.Config.Argb8888!);
        using (var canvas = new Canvas(bitmap)) {
            icon.SetBounds(0, 0, IconSize, IconSize);
            icon.Draw(canvas);
        }
        using var stream = new MemoryStream();
        if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream))
            return "";

        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}
