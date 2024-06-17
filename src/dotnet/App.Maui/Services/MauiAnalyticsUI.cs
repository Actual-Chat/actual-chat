using ActualChat.UI.Blazor.Services;
using Plugin.Firebase.Analytics;

namespace ActualChat.App.Maui.Services;

public class MauiAnalyticsUI : IAnalyticsUI
{
    public Task<bool> IsConfigured(CancellationToken cancellationToken)
        => Task.FromResult(Preferences.Default.ContainsKey(Constants.Preferences.EnableAnalytics));

    public Task UpdateAnalyticsState(bool isEnabled, CancellationToken cancellationToken)
    {
        Preferences.Default.Set(Constants.Preferences.EnableAnalytics, isEnabled);
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isEnabled;
        return Task.CompletedTask;
    }
}
