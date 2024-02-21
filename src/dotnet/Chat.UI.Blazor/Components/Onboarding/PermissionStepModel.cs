using ActualChat.Streaming.UI.Blazor.Components;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.Notification.UI.Blazor;
using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public sealed class PermissionStepModel(IServiceProvider services)
{
    public readonly HostInfo HostInfo = services.HostInfo();
    public readonly MicrophonePermissionHandler MicrophonePermission
        = services.GetRequiredService<AudioRecorder>().MicrophonePermission;
    public readonly INotificationsPermission NotificationsPermission
        = services.GetRequiredService<INotificationsPermission>();
    public readonly ContactsPermissionHandler ContactsPermission
        = services.GetRequiredService<ContactsPermissionHandler>();
    public bool IsMobile => HostInfo.AppKind.IsMobile();

    public bool SkipMicrophonePermission { get; set; }
    public bool SkipNotificationsPermission { get; set; }
    public bool SkipContactsPermission { get; set; }
    public bool RequestMicrophonePermission { get; set; }
    public bool RequestNotificationsPermission { get; set; }
    public bool RequestContactsPermission { get; set; }

    public bool SkipEverything => SkipMicrophonePermission & SkipNotificationsPermission & SkipContactsPermission;

    public static async Task<PermissionStepModel> New(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var m = new PermissionStepModel(services);
        m.SkipMicrophonePermission = await m.MicrophonePermission.Check(cancellationToken) == true;
        m.SkipNotificationsPermission = !m.IsMobile // See the note below
            || await m.NotificationsPermission.IsGranted(cancellationToken) == true;
        m.SkipContactsPermission = await m.ContactsPermission.Check(cancellationToken) == true;
        m.RequestMicrophonePermission = !m.SkipContactsPermission;
        m.RequestNotificationsPermission = !m.SkipNotificationsPermission;
        m.RequestContactsPermission = !m.SkipContactsPermission;
        return m;

        // NOTE(AY): Requesting mic & notifications in the same event handler doesn't work on web.
        // We should show an extra popup explaining that notification permission request
        // may not appear + that the user has to click on the "Notifications blocked" item
        // in the browser bar to enable them.
        //
        // Notification permission request handler must be bound to the button there,
        // coz here it just doesn't have a chance to work properly due to other
        // permission requests.
        //
        // I disabled this logic for web browser for now, coz it doesn't work anyway,
        // and we show NotificationsPermissionBanner which allows to enable it later -
        // which, by the way, should fallback to the same popup.
    }

    public void MarkCompleted()
    {
        var onboardingUI = services.GetRequiredService<OnboardingUI>();
        onboardingUI.UpdateLocalSettings(onboardingUI.LocalSettings.Value with {
            IsPermissionsStepCompleted = true,
        });
    }
}
