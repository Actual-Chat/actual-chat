using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using CoreFoundation;
using Foundation;
using PushKit;
using DeviceType = ActualChat.Notifications.DeviceType;
using MessageDataKeys = ActualChat.Constants.Notification.MessageDataKeys;

namespace ActualChat.App.Maui;

/// <summary>
/// PushKit VoIP registration and delivery. Every push must report a call to CallKit
/// before returning, or iOS kills the app and stops delivering VoIP pushes to it.
/// </summary>
public class IosVoipPushes : PKPushRegistryDelegate
{
    public static IosVoipPushes Instance { get; } = new();

    private readonly PKPushRegistry _registry = new(DispatchQueue.MainQueue);
    private ILogger Log => field ??= StaticLog.For<IosVoipPushes>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.IosCalls);

    public void Initialize()
    {
        _registry.Delegate = this;
        _registry.DesiredPushTypes = new NSSet<NSString>(PKPushType.Voip);
    }

    public override void DidUpdatePushCredentials(
        PKPushRegistry registry, PKPushCredentials credentials, string type)
    {
        var token = Convert.ToHexString(credentials.Token.ToArray());
        if (token.IsNullOrEmpty()) {
            Log.LogError("DidUpdatePushCredentials: empty token");
            return;
        }

        Log.LogInformation("DidUpdatePushCredentials: token received, length={Length}", token.Length);
        _ = DispatchToBlazor(async c => {
                // The token arrives before sign-in on a fresh install, and registering it
                // against a guest account silently binds it to nobody.
                var accountUI = c.GetRequiredService<AccountUI>();
                await accountUI.WhenReady.ConfigureAwait(false);
                await accountUI.OwnAccount.Computed
                    .When(x => !x.IsGuest, CancellationToken.None)
                    .ConfigureAwait(false);

                DebugLog?.LogInformation("DidUpdatePushCredentials: registering the VoIP token");
                var mauiNotifications = c.GetRequiredService<MauiNotifications>();
                await mauiNotifications
                    .RefreshNotificationToken(token, DeviceType.iOSVoipApp)
                    .ConfigureAwait(false);
            },
            "DidUpdatePushCredentials");
    }

    public override void DidReceiveIncomingPush(
        PKPushRegistry registry, PKPushPayload payload, string type, Action completion)
    {
        var dict = payload.DictionaryPayload;
        var conversationId = ConversationId.TryParse(
            dict[MessageDataKeys.ConversationId]?.ToString(), allowNull: true);
        var callerName = dict[MessageDataKeys.CallerName]?.ToString() ?? "";
        var hasVideo = dict[MessageDataKeys.HasVideo] is NSNumber { BoolValue: true };
        Log.LogInformation("DidReceiveIncomingPush: {ConversationId}, {CallerName}, hasVideo={HasVideo}",
            conversationId, callerName, hasVideo);
        // Reporting is not optional and cannot be deferred to a scope that may not exist:
        // a push that returns without one costs the app its VoIP delivery.
        IosCalls.Instance.ReportIncomingCall(conversationId, callerName, hasVideo, completion);
    }
}
