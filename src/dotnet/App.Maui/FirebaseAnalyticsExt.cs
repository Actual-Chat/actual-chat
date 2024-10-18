using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components.Routing;
using Plugin.Firebase.Analytics;

namespace ActualChat.App.Maui;

public static class FirebaseAnalyticsExt
{
    public static void ActivateOwnAnalyticsCollection(IServiceProvider serviceProvider)
    {
#if IOS || ANDROID
        _ = new Collector(serviceProvider);
#endif
    }

    // Nested types

    private class Collector
    {
        private readonly History _history;
        private string _location;
        private readonly IFirebaseAnalytics _firebaseAnalytics;
        private readonly bool _isMauiApp;
        private readonly string _appKind;

        public Collector(IServiceProvider serviceProvider)
        {
            _firebaseAnalytics = CrossFirebaseAnalytics.Current;
            _isMauiApp = Constants.HostInfo.HostKind.IsMauiApp();
            _appKind = Constants.HostInfo.AppKind.ToString();
            _history = serviceProvider.GetRequiredService<History>();
            _location = _history.Uri;
            _history.LocationChanged += OnLocationChanged;
        }

        private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            var location = _history.Uri;
            if (OrdinalEquals(location, _location))
                return; // Location has not changed. Apparently panel/modal/menu has been opened/closed.

            var parameters = new Dictionary<string, object> {
                {"page_location", location },
                {"page_referrer", _location },
                {"page_title", "Actual Chat" },
                {"isMauiApp", _isMauiApp },
            };
            if (_isMauiApp)
                parameters.Add("appKind", _appKind);
            _firebaseAnalytics.LogEvent("page_view", parameters);
            _location = location;
        }
    }
}
