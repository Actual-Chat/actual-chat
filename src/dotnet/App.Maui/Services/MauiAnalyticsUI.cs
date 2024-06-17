using ActualChat.UI.Blazor.Services;
using Plugin.Firebase.Analytics;

namespace ActualChat.App.Maui.Services;

public class MauiAnalyticsUI : IAnalyticsUI
{
    public Task UpdateAnalyticsState(bool isEnabled)
    {
        Preferences.Default.Set(Constants.Preferences.EnableAnalytics, isEnabled);
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isEnabled;
        return Task.CompletedTask;
    }
}
