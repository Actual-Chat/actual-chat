using ActualChat.App.Maui.Services;
using ActualChat.Notification;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Generators;
using CoreFoundation;
using Foundation;
using PushKit;
using DeviceType = ActualChat.Notification.DeviceType;

namespace ActualChat.App.Maui;

public class IosVoipPushes : PKPushRegistryDelegate
{
    private readonly PKPushRegistry _registry = new (DispatchQueue.MainQueue);
    public static readonly IosVoipPushes Instance = new ();
    private ILogger Log => field ??= StaticLog.For(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.VoipPushes);

    public void Initialize()
    {
        _registry.Delegate = Instance;
        _registry.DesiredPushTypes = new NSSet<NSString>(PKPushType.Voip);
    }

    public override void DidUpdatePushCredentials(PKPushRegistry registry, PKPushCredentials credentials, string type)
    {
        var token = credentials.Token.ToArray();
        var sToken = Convert.ToHexString(token);
        if (sToken.IsNullOrEmpty()) {
            Log.LogError("DidUpdatePushCredentials: Empty token");
            return;
        }

        Log.LogInformation("DidUpdatePushCredentials: token received, length={Length}", sToken.Length);
        _ = DispatchToBlazor(async c => {
                DebugLog?.LogInformation("DidUpdatePushCredentials: waiting for sign-in");
                var accountUI = c.GetRequiredService<AccountUI>();
                await accountUI.WhenReady.ConfigureAwait(false);
                await accountUI.OwnAccount.Computed
                    .When(x => !x.IsGuest, CancellationToken.None) // TODO: inherit from ProcessorBase and use StopToken
                    .ConfigureAwait(false);

                DebugLog?.LogInformation("DidUpdatePushCredentials: refreshing token");
                var mauiNotifications = c.GetRequiredService<MauiNotifications>();
                await mauiNotifications.RefreshNotificationToken(sToken, DeviceType.iOSApp, NotificationChannel.Call)
                    .ConfigureAwait(false);
            },
            "DidUpdatePushCredentials");
    }

    public override void DidReceiveIncomingPush(
        PKPushRegistry registry,
        PKPushPayload payload,
        string type,
        Action completion)
    {
        DebugLog?.LogInformation("DidReceiveIncomingPush");
        var dict = payload.DictionaryPayload;
        var uuid = new NSUuid(dict["uuid"]?.ToString() ?? RandomStringGenerator.Default.Next(16));
        var callerName = dict["callerName"]?.ToString() ?? "Unknown";
        var phone = Phone.ParseNullable(dict["phone"]?.ToString());
        var email = dict["email"]?.ToString();
        var hasVideo = dict["hasVideo"] is NSNumber { BoolValue: true };
        var chatId = ChatId.Parse(dict["chatId"]?.ToString());

        Log.LogInformation($"DidReceiveIncomingPush: reporting incoming call {uuid}, {callerName}, {phone}, {email}, {hasVideo}, {chatId}");
        IosCalls.Instance.ReportIncomingCall(
            uuid,
            chatId,
            phone,
            email,
            callerName,
            hasVideo,
            completion);
    }
}
