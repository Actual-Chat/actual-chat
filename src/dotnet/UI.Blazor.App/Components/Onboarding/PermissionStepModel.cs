using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class PermissionStepModel(IServiceProvider services)
{
    public IReadOnlyList<PermissionRow> Rows { get; private set; } = [];
    public bool SkipEverything => Rows.All(r => !r.IsVisible);

    public static async Task<PermissionStepModel> New(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var hostInfo = services.HostInfo();
        var microphonePermission = services.GetRequiredService<AudioRecorder>().MicrophonePermission;
        var notificationsPermission = services.GetRequiredService<INotificationsPermission>();
        var batteryOptimization = services.GetService<BatteryOptimizationHandler>();

        var isMicrophoneGranted = await microphonePermission.Check(cancellationToken) == true;
        // Notifications are mobile-only here: on web the permission prompt must be bound
        // to a direct user gesture in JS, which this flow can't guarantee, and
        // NotificationsPermissionBanner allows enabling it later anyway.
        var isNotificationsVisible = hostInfo.AppKind.IsMobile()
            && await notificationsPermission.IsGranted(cancellationToken) != true;
        // The handler is Android-only; iOS has no battery-optimization equivalent —
        // APNs/PushKit delivery isn't gated on a per-app battery setting.
        var isBatteryOptimizationVisible = batteryOptimization != null
            && await batteryOptimization.Check(cancellationToken) != true;

        // Other permissions are requested contextually: camera on video start,
        // contacts in contact-related UIs, location on "Share live location".
        var m = new PermissionStepModel(services);
        m.Rows = [
            new PermissionRow(
                "Microphone",
                $"Live-transcribed voice messaging is where {CoreConstants.AppName} shines, "
                + "but this feature won't work without microphone access.",
                "icon-mic",
                ct => microphonePermission.CheckOrRequest(true, false, ct).AsTask()) {
                IsVisible = !isMicrophoneGranted,
            },
            new PermissionRow(
                "Notifications",
                "Get notified about new messages. Any chat can be muted individually.",
                "icon-bell",
                async ct => {
                    await notificationsPermission.Request(ct);
                    return await notificationsPermission.IsGranted(ct) == true;
                }) {
                IsVisible = isNotificationsVisible,
            },
            new PermissionRow(
                "Background activity",
                $"Keeps calls ringing when Android tries to put {CoreConstants.AppName} to sleep.",
                "icon-battery",
                ct => batteryOptimization!.CheckOrRequest(true, false, ct).AsTask()) {
                IsVisible = isBatteryOptimizationVisible,
            },
        ];
        return m;
    }

    public void MarkCompleted()
    {
        var onboardingUI = services.GetRequiredService<OnboardingUI>();
        onboardingUI.UpdateLocalSettings(onboardingUI.LocalSettings.Value with {
            IsPermissionsStepCompleted = true,
        });
    }
}

public sealed class PermissionRow(
    string title,
    string rationale,
    string icon,
    Func<CancellationToken, Task<bool>> request)
{
    public string Title { get; } = title;
    public string Rationale { get; } = rationale;
    public string Icon { get; } = icon;
    public bool IsVisible { get; init; }
    public bool IsGranted { get; set; }

    public Task<bool> Request(CancellationToken cancellationToken = default)
        => request.Invoke(cancellationToken);
}
