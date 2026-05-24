using ActualChat.UI.Blazor.Services;
#if IOS || ANDROID
using Plugin.Firebase.Analytics;
#endif

namespace ActualChat.App.Maui.Services;

public class MauiDataCollectionSettingsUI : IDataCollectionSettingsUI
{
    public Task<bool> IsConfigured(CancellationToken cancellationToken)
        => Task.FromResult(MauiPreferences.IsDataCollectionEnabled.HasValue);

    public Task UpdateState(bool isEnabled, CancellationToken cancellationToken)
    {
        MauiPreferences.IsDataCollectionEnabled = isEnabled;
#if IOS || ANDROID
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isEnabled;
        MauiDiagnostics.SetIsAnalyticsCollectionEnabled(isEnabled);
#endif
        return Task.CompletedTask;
    }
}
