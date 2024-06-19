using ActualChat.UI.Blazor.Services;
#if IOS
using Plugin.Firebase.Analytics;
#elif ANDROID
using Plugin.Firebase.Analytics;
#endif

namespace ActualChat.App.Maui.Services;

public class MauiAnalyticsUI : IAnalyticsUI
{
    public Task<bool> IsConfigured(CancellationToken cancellationToken)
        => Task.FromResult(Preferences.Default.ContainsKey(Constants.Preferences.EnableAnalytics));

    public Task UpdateAnalyticsState(bool isEnabled, CancellationToken cancellationToken)
    {
        Preferences.Default.Set(Constants.Preferences.EnableAnalytics, isEnabled);
#if IOS || ANDROID
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isEnabled;
#endif
        return Task.CompletedTask;
    }
}
