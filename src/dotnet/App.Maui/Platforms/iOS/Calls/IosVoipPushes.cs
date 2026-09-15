using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App;
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
    private string _token = "";
    private ILogger Log => field ??= StaticLog.For<IosVoipPushes>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.IosCalls);

    public void Initialize()
    {
        _registry.Delegate = this;
        _registry.DesiredPushTypes = new NSSet<NSString>(PKPushType.Voip);
    }

    // Once per scope: a sign-out removes the token's row on the server along with the FCM one,
    // but PushKit re-delivers the credentials only on launch, so the next sign-in must re-register
    // the token it already has.
    public Task RegisterToken(IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        var token = Volatile.Read(ref _token);
        return token.IsNullOrEmpty()
            ? Task.CompletedTask
            : RegisterToken(scopedServices, token, cancellationToken);
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
        Volatile.Write(ref _token, token);
        _ = DispatchToBlazor(
            c => RegisterToken(c, token, c.AppUIHub().StopToken),
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

    // Private methods

    private async Task RegisterToken(
        IServiceProvider scopedServices, string token, CancellationToken cancellationToken)
    {
        // Registering before sign-in would bind the token to nobody. A launch registers twice,
        // from here and from the bridge, which the server treats as one refresh.
        var accountUI = scopedServices.GetRequiredService<AccountUI>();
        await accountUI.WhenReady.ConfigureAwait(false);
        await accountUI.OwnAccount.Computed
            .When(x => !x.IsGuest, cancellationToken)
            .ConfigureAwait(false);

        DebugLog?.LogInformation("RegisterToken: registering the VoIP token");
        var mauiNotifications = scopedServices.GetRequiredService<MauiNotifications>();
        await mauiNotifications
            .RefreshNotificationToken(token, DeviceType.iOSVoipApp, cancellationToken)
            .ConfigureAwait(false);
    }
}
