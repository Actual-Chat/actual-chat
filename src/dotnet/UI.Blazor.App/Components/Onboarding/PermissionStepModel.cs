using ActualChat.UI.Blazor.App.Services;
using ActualChat.Hosting;
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
        var contactsPermission = services.GetRequiredService<ContactsPermissionHandler>();
        var locationPermission = services.GetRequiredService<LocationPermissionHandler>();
        var batteryOptimization = services.GetService<BatteryOptimizationHandler>();

        var isMicrophoneGranted = await microphonePermission.Check(cancellationToken) == true;
        // Notifications are mobile-only here: on web the permission prompt must be bound
        // to a direct user gesture in JS, which this flow can't guarantee, and
        // NotificationsPermissionBanner allows enabling it later anyway.
        var isNotificationsVisible = hostInfo.AppKind.IsMobile()
            && await notificationsPermission.IsGranted(cancellationToken) != true;
        var isContactsGranted = await contactsPermission.Check(cancellationToken) == true;
        var isLocationGranted = await locationPermission.Check(cancellationToken) == true;
        var isBatteryOptimizationVisible = batteryOptimization != null
            && await batteryOptimization.Check(cancellationToken) != true;

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
                "We send notifications related to the chats you've joined, "
                + "and you can mute these notifications on a per-chat basis.",
                "icon-bell",
                async ct => {
                    await notificationsPermission.Request(ct);
                    return await notificationsPermission.IsGranted(ct) == true;
                }) {
                IsVisible = isNotificationsVisible,
            },
            new PermissionRow(
                "Contacts",
                $"{CoreConstants.AppName} can import your contacts to help you find friends "
                + "who are already using the service.<br/>"
                + "If you grant access to your contact list, we will send the contact names and the hashed values "
                + "of phone numbers and email addresses to our servers. "
                + "This is sufficient for matching purposes, but not enough to restore your actual contacts.",
                "icon-people",
                ct => contactsPermission.CheckOrRequest(mustRequest: true, mustTroubleshoot: false, ct).AsTask()) {
                IsVisible = !isContactsGranted,
            },
            new PermissionRow(
                "Location",
                "Share your live location in a chat so others can follow you in real time.",
                "icon-map-point",
                ct => locationPermission.CheckOrRequest(true, false, ct).AsTask()) {
                IsVisible = !isLocationGranted,
            },
            new PermissionRow(
                "Background activity",
                $"Android may put {CoreConstants.AppName} to sleep to save battery, which can delay or drop "
                + "incoming calls and voice messages. Allow unrestricted battery usage so calls always ring.",
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
    string subtitle,
    string icon,
    Func<CancellationToken, Task<bool>> request)
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public string Icon { get; } = icon;
    public bool IsVisible { get; init; }
    public bool IsGranted { get; set; }

    public Task<bool> Request(CancellationToken cancellationToken = default)
        => request.Invoke(cancellationToken);
}
