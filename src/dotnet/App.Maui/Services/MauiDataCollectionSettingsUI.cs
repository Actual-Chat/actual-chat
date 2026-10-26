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
#if ANDROID
        // A pending init reads the preference above when it completes
        if (!MauiProgram.IsFirebaseAnalyticsReady)
            return Task.CompletedTask;
#endif
#if IOS || ANDROID
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isEnabled;
        MauiDiagnostics.SetIsAnalyticsCollectionEnabled(isEnabled);
#endif
        return Task.CompletedTask;
    }

    public Task<(AnalyticsClientKind Kind, string Id)?> GetAnalyticsClientId(CancellationToken cancellationToken)
    {
        if (MauiPreferences.IsDataCollectionEnabled != true)
            return Task.FromResult<(AnalyticsClientKind Kind, string Id)?>(null);
#if ANDROID
        if (!MauiProgram.IsFirebaseAnalyticsReady)
            return Task.FromResult<(AnalyticsClientKind Kind, string Id)?>(null);

        return Get(AnalyticsClientKind.Android);
#elif IOS
        return Get(AnalyticsClientKind.Ios);
#else
        return Task.FromResult<(AnalyticsClientKind Kind, string Id)?>(null);
#endif

#if IOS || ANDROID
        static async Task<(AnalyticsClientKind Kind, string Id)?> Get(AnalyticsClientKind kind) {
            var id = await CrossFirebaseAnalytics.Current.GetAppInstanceIdAsync().ConfigureAwait(false);
            return id.IsNullOrEmpty() ? null : (kind, id);
        }
#endif
    }
}
