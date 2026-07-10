namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Tracks whether the app is exempt from OS battery optimization (Android Doze);
/// "granted" means the app can run in background to receive calls reliably.
/// </summary>
public abstract class BatteryOptimizationHandler(UIHub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart);
