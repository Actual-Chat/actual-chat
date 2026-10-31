using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Foundation;
using UIKit;

namespace ActualChat.App.Maui;

public sealed class AppleMapOpener(AppUIHub hub) : MauiMapOpener(hub)
{
    private const string GoogleMapsScheme = "comgooglemaps";
    private const string IconUrlPrefix = "/dist/images/map-apps/";

    // UrlFormat arguments: {0} latitude, {1} longitude, {2} escaped label, {3} this app's bundle id
    // Ordered global-first, then by region, with the ride-hailing apps last
    // Every scheme must also be in Platforms/iOS/Info.plist's LSApplicationQueriesSchemes,
    // or CanOpenUrl reports the app as missing
    private static readonly MapApp[] KnownMapApps = [
        // No label: Google Maps takes q as a search term rather than a pin caption,
        // so a person's name would navigate to whatever it matches
        NewMapApp(GoogleMapsScheme, "Google Maps", "google-maps",
            "comgooglemaps://?q={0},{1}&center={0},{1}&zoom=16"),
        NewMapApp("maps", "Apple Maps", "apple-maps", "maps://?ll={0},{1}&q={2}"),
        NewMapApp("waze", "Waze", "waze", "waze://?ll={0},{1}"),
        NewMapApp("here-location", "HERE WeGo", "here-wego", "here-location://{0},{1},{2}"),
        NewMapApp("com.sygic.aura", "Sygic", "sygic", "com.sygic.aura://coordinate|{1}|{0}|show"),
        NewMapApp("tomtomgo", "TomTom GO", "tomtom-go", "tomtomgo://x-callback-url/navigate?destination={0},{1}"),
        NewMapApp("magicearth", "Magic Earth", "magic-earth", "magicearth://?show_on_map&lat={0}&lon={1}&name={2}"),
        NewMapApp("om", "Organic Maps", "organic-maps", "om://map?v=1&ll={0},{1}&n={2}"),
        NewMapApp("mapsme", "Maps.me", "maps-me", "mapsme://map?v=1&ll={0},{1}&n={2}"),
        NewMapApp("osmandmaps", "OsmAnd", "osmand", "osmandmaps://?lat={0}&lon={1}&z=16"),
        NewMapApp("moovit", "Moovit", "moovit", "moovit://directions?dest_lat={0}&dest_lon={1}&dest_name={2}"),
        NewMapApp("citymapper", "Citymapper", "citymapper", "citymapper://directions?endcoord={0},{1}&endname={2}"),
        NewMapApp("yandexmaps", "Yandex Maps", "yandex-maps", "yandexmaps://maps.yandex.ru/?pt={1},{0}&z=16"),
        NewMapApp("yandexnavi", "Yandex Navigator", "yandex-navigator",
            "yandexnavi://show_point_on_map?lat={0}&lon={1}&zoom=16"),
        NewMapApp("dgis", "2GIS", "2gis", "dgis://2gis.ru/geo/{1},{0}"),
        // The dev/coord_type flags below say "these are raw GPS coordinates", so the app applies
        // the GCJ-02/BD-09 shift itself - unflagged WGS84 lands a few hundred meters off in China
        NewMapApp("iosamap", "Amap", "amap",
            "iosamap://viewMap?sourceApplication={3}&poiname={2}&lat={0}&lon={1}&dev=1"),
        NewMapApp("baidumap", "Baidu Maps", "baidu-maps",
            "baidumap://map/marker?location={0},{1}&title={2}&coord_type=wgs84&src={3}"),
        NewMapApp("qqmap", "Tencent Maps", "tencent-maps",
            "qqmap://map/marker?marker=coord:{0},{1};title:{2}&coord_type=1&referer={3}"),
        NewMapApp("nmap", "Naver Map", "naver-map", "nmap://place?lat={0}&lng={1}&name={2}&appname={3}"),
        NewMapApp("kakaomap", "KakaoMap", "kakaomap", "kakaomap://look?p={0},{1}"),
        // The brackets are percent-encoded because NSUrl rejects them as they are
        NewMapApp("uber", "Uber", "uber",
            "uber://?action=setPickup&pickup=my_location"
            + "&dropoff%5Blatitude%5D={0}&dropoff%5Blongitude%5D={1}&dropoff%5Bnickname%5D={2}"),
    ];

    protected override Task<IReadOnlyList<MapApp>> GetPlatformApps()
    {
        return DispatchToMainThread(FetchInstalledMapApps);

        IReadOnlyList<MapApp> FetchInstalledMapApps()
        {
            var application = UIApplication.SharedApplication;
            return KnownMapApps.Where(x => application.CanOpenUrl(new NSUrl(x.Key + "://")))
                .DistinctBy(x => x.Title)
                .ToList();
        }
    }

    protected override async Task<bool> TryOpen(MapApp mapApp, GeoPoint point, string? name)
    {
        if (mapApp.UrlFormat.IsNullOrEmpty())
            return false;

        var lat = point.LatitudeText();
        var lon = point.LongitudeText();
        var label = Uri.EscapeDataString(name.NullIfEmpty() ?? point.ToCoordinatesText());
        var url = string.Format(mapApp.UrlFormat, lat, lon, label, AppInfo.Current.PackageName);
        return await UIApplication.SharedApplication
            .OpenUrlAsync(new NSUrl(url), new UIApplicationOpenUrlOptions())
            .ConfigureAwait(true);
    }

    // Private methods

    private static MapApp NewMapApp(string scheme, string title, string icon, string urlFormat)
        => new(scheme, title, IconUrlPrefix + icon + ".png", urlFormat);
}
