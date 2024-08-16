using ActualChat.UI.Blazor.Services;
#if IOS
using Plugin.Firebase.Analytics;
#elif ANDROID
using Plugin.Firebase.Analytics;
#endif

namespace ActualChat.App.Maui.Services;

public class MauiDataCollectionSettingsUI : IDataCollectionSettingsUI
{
    public Task<bool> IsConfigured(CancellationToken cancellationToken)
        => Task.FromResult(Preferences.Default.ContainsKey(Constants.Preferences.EnableDataCollectionKey));

    public Task UpdateState(bool isEnabled, CancellationToken cancellationToken)
    {
        Preferences.Default.Set(Constants.Preferences.EnableDataCollectionKey, isEnabled);
#if IOS || ANDROID
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isEnabled;
#endif
        return Task.CompletedTask;
    }
}
