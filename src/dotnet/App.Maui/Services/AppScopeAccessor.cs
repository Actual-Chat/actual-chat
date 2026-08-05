namespace ActualChat.App.Maui.Services;

/// <summary>
/// The scope a static Android component should talk to: the WebView scope when the UI
/// is up, the headless wake scope otherwise.
/// </summary>
public static class AppScopeAccessor
{
    public static IServiceProvider? Current
        => AppServicesAccessor.TryGetScopedServices(out var services)
            ? services
            : HeadlessBlazorScope.Current?.Services;
}
