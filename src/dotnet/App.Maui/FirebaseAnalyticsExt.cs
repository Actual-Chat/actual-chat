using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components.Routing;
#if IOS || ANDROID
using Plugin.Firebase.Analytics;
#endif

namespace ActualChat.App.Maui;

/// <summary>
/// Firebase Analytics event tracking for iOS and Android platforms.
/// </summary>
public static class FirebaseAnalyticsExt
{
    public static void ActivateOwnAnalyticsCollection(IServiceProvider serviceProvider)
    {
#if IOS || ANDROID
        _ = new Collector(serviceProvider.AppUIHub());
#endif
    }

    // Nested types
#if IOS || ANDROID
    private class Collector
    {
        private readonly History _history;
        private string _location;
        private readonly IFirebaseAnalytics _firebaseAnalytics;
        private readonly bool _isMauiApp;
        private readonly string _appKind;
        private readonly AccountUI _accountUI;

        public Collector(AppUIHub hub)
        {
            _firebaseAnalytics = CrossFirebaseAnalytics.Current;
            _isMauiApp = Constants.HostInfo.HostKind.IsMauiApp();
            _appKind = Constants.HostInfo.AppKind.ToString();
            _accountUI = hub.AccountUI;
            _history = hub.History;
            _location = _history.Uri;
            _history.LocationChanged += OnLocationChanged;
            var analyticEvents = hub.AnalyticEvents;
            analyticEvents.ModalStateChanged += OnAnalyticEventsOnModalStateChanged;
            analyticEvents.MessagePosted += OnMessagePosted;
            analyticEvents.RecordingStarted += OnRecordingStarted;
            analyticEvents.RecordingCompleted += OnRecordingCompleted;
            analyticEvents.VideoStreamStarted += OnVideoStreamStarted;
        }

        private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            var location = _history.Uri;
            if (location == _location)
                return; // Location has not changed. Apparently panel/modal/menu has been opened/closed.

            var parameters = CreateBaseParameters();
            parameters.Add("page_referrer", _location);
            parameters.Add("page_title", CoreConstants.AppName); // NOTE: to mimic automatic firebase events
            LogEvent("page_view", parameters);
            _location = location;
        }

        private void OnAnalyticEventsOnModalStateChanged(object? sender, AnalyticEvents.ModalStateChangedEventArgs e)
        {
            var parameters = CreateBaseParameters();
            parameters.Add("modal_name", e.ModalName);
            LogEvent(e.IsOpen ? "modal_opened" : "modal_closed", parameters);
        }

        private void OnMessagePosted(object? sender, AnalyticEvents.MessagePostedEventArgs e)
        {
            var parameters = CreateBaseParameters();
            parameters.Add("has_text", e.HasText);
            parameters.Add("attachment_count", e.AttachmentCount);
            parameters.Add("is_reply", e.IsReply);
            LogEvent("message_posted", parameters);
        }

        private void OnRecordingStarted(object? sender, EventArgs e)
        {
            var parameters = CreateBaseParameters();
            LogEvent("recording_started", parameters);
        }

        private void OnRecordingCompleted(object? sender, AnalyticEvents.RecordingCompletedEventArgs e)
        {
            var parameters = CreateBaseParameters();
            parameters.Add("duration_ms", e.DurationInMs);
            LogEvent("recording_completed", parameters);
        }

        private void OnVideoStreamStarted(object? sender, AnalyticEvents.VideoStreamStartedEventArgs e)
        {
            var parameters = CreateBaseParameters();
            parameters.Add("source_kind", e.SourceKind.ToString());
            LogEvent("video_stream_started", parameters);
        }

        private void LogEvent(string eventName, IDictionary<string, object> parameters)
            => _firebaseAnalytics.LogEvent(eventName, parameters);

        private Dictionary<string, object> CreateBaseParameters()
        {
            var parameters = new Dictionary<string, object>() {
                {"page_location", _history.Uri },
                {"isMauiApp", _isMauiApp },
                {"isAdmin", _accountUI.OwnAccount.Value.IsAdmin },
            };
            if (_isMauiApp)
                parameters.Add("appKind", _appKind);
            return parameters;
        }
    }
#endif
}
