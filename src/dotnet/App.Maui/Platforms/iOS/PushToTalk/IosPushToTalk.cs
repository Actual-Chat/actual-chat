using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services;
using ActualChat.Notifications;
using ActualChat.Security;
using AVFoundation;
using Foundation;
using PushToTalk;
using UIKit;
using DeviceType = ActualChat.Notifications.DeviceType;

namespace ActualChat.App.Maui;

/// <summary>
/// Process-level Apple Push to Talk integration: one aggregate "Voxt" channel whose join
/// survives app kill/reboot; incoming PTT pushes route into <see cref="WalkieTalkieSession"/>.
/// Receive-only: the channel runs in ListenOnly transmission mode.
/// </summary>
public static class IosPushToTalk
{
    public const string ChannelName = "Voxt";
    private static readonly NSUuid ChannelUuid = new("f3b9a7e2-4c15-4a8e-9f2d-7b6c5d4e3f21");

    private static readonly Lock Lock = new();
    private static PTChannelManager? _manager;
    private static ManagerDelegate? _managerDelegate;
    private static RestorationDelegate? _restorationDelegate;
    private static volatile string _pttToken = "";
    private static volatile PendingWake? _pendingWake;
    private static ILogger Log => field ??= StaticLog.For(typeof(IosPushToTalk));

    public static void Initialize()
    {
        lock (Lock) {
            if (_managerDelegate is not null)
                return;

            _managerDelegate = new ManagerDelegate();
            _restorationDelegate = new RestorationDelegate();
        }
        PTChannelManager.Create(_managerDelegate, _restorationDelegate, (manager, error) => {
            if (error is not null) {
                Log.LogError("PTChannelManager.Create failed: {Error}", error.LocalizedDescription);
                return;
            }

            lock (Lock)
                _manager = manager;
            Log.LogInformation("PTChannelManager ready");
        });
    }

    public static void EnsureJoined()
    {
        var manager = _manager;
        if (manager is null || manager.ActiveChannelUuid is not null)
            return;

        Log.LogInformation("Joining the PTT channel");
        manager.RequestJoinChannel(ChannelUuid, NewDescriptor());
    }

    public static void Leave()
    {
        var manager = _manager;
        if (manager?.ActiveChannelUuid is null)
            return;

        Log.LogInformation("Leaving the PTT channel");
        manager.LeaveChannel(ChannelUuid);
    }

    public static void ClearActiveParticipant()
    {
        var manager = _manager;
        if (manager is null)
            return;

        manager.SetActiveRemoteParticipant(null!, ChannelUuid, error => {
            if (error is not null)
                Log.LogWarning("SetActiveRemoteParticipant(null) failed: {Error}", error.LocalizedDescription);
        });
    }

    // Private methods

    private static PTChannelDescriptor NewDescriptor()
        => new(ChannelName, UIImage.FromBundle("AppIcon"));

    private static void RegisterToken(string token)
    {
        BlazorWebViewApp.EnsureStarted();
        _pttToken = token;
        _ = BackgroundTask.Run(async () => {
            var app = await BlazorWebViewApp.WhenAppReady.ConfigureAwait(false);
            // MauiNotifications lives in the app container, not the MAUI root container.
            var mauiNotifications = app.Services.GetRequiredService<MauiNotifications>();
            await mauiNotifications.RefreshNotificationToken(token, DeviceType.iOSPttApp, CancellationToken.None)
                .ConfigureAwait(false);
        }, Log, "PTT token registration failed", CancellationToken.None);
    }

    private static void DeregisterToken()
    {
        var token = _pttToken;
        _pttToken = "";
        if (token.IsNullOrEmpty())
            return;

        _ = BackgroundTask.Run(async () => {
            var app = await BlazorWebViewApp.WhenAppReady.ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            var session = await sessionResolver.SessionTask.ConfigureAwait(false);
            var commander = app.Services.GetRequiredService<ICommander>();
            await commander.Call(new Notifications_DeregisterDevice(session, token), CancellationToken.None)
                .ConfigureAwait(false);
        }, Log, "PTT token deregistration failed", CancellationToken.None);
    }

    private static void OnAudioSessionActivated()
    {
        AudioSession.IsExternallyActivated = true;
        var wake = Interlocked.Exchange(ref _pendingWake, null);
        if (wake is null)
            return;

        BlazorWebViewApp.EnsureStarted();
        _ = BackgroundTask.Run(async () => {
            var isForeground = await AppServicesAccessor
                .DispatchToMainThread(() => UIApplication.SharedApplication.ApplicationState
                    == UIApplicationState.Active)
                .ConfigureAwait(false);
            await WalkieTalkieSession.HandleWake(wake.ChatId, wake.StartedAt, isForeground, IosPlatform.Instance)
                .ConfigureAwait(false);
        }, Log, "PTT wake failed", CancellationToken.None);
    }

    // Nested types

    private sealed record PendingWake(ChatId ChatId, Moment StartedAt);

    private sealed class IosPlatform : WalkieTalkiePlatform
    {
        public static readonly IosPlatform Instance = new();

        public override void OnWakeFailed(ChatId chatId)
            => ClearActiveParticipant();

        public override void OnHeadlessTeardown()
            => ClearActiveParticipant();

        public override Task OnForegroundWakeHandled(ChatId chatId)
        {
            // Foreground: the app manages its own session; end the PTT transmission right away.
            ClearActiveParticipant();
            return Task.CompletedTask;
        }
    }

    private sealed class ManagerDelegate : PTChannelManagerDelegate
    {
        public override void DidJoinChannel(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelJoinReason reason)
        {
            Log.LogInformation("PTT channel joined ({Reason})", reason);
            // Receive-only v1: no transmit button in the system UI.
            channelManager.SetTransmissionMode(PTTransmissionMode.ListenOnly, channelUuid, error => {
                if (error is not null)
                    Log.LogWarning("SetTransmissionMode failed: {Error}", error.LocalizedDescription);
            });
        }

        public override void DidLeaveChannel(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelLeaveReason reason)
        {
            // A leave can tear the session down without DidDeactivateAudioSession;
            // a stuck flag would permanently disable the app's own session activation.
            AudioSession.IsExternallyActivated = false;
            Log.LogInformation("PTT channel left ({Reason})", reason);
            DeregisterToken();
        }

        public override void DidBeginTransmitting(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelTransmitRequestSource source)
        { }

        public override void DidEndTransmitting(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelTransmitRequestSource source)
        { }

        public override void ReceivedEphemeralPushToken(PTChannelManager channelManager, NSData pushToken)
        {
            var token = Convert.ToHexString(pushToken.ToArray()).ToLower();
            Log.LogInformation("PTT push token received ({Length} bytes)", pushToken.Length);
            RegisterToken(token);
        }

        public override PTPushResult IncomingPushResult(
            PTChannelManager channelManager, NSUuid channelUuid, NSDictionary<NSString, NSObject> pushPayload)
        {
            // Must return synchronously and fast; playback starts in DidActivateAudioSession.
            var chatSid = GetString(pushPayload, Constants.Notification.MessageDataKeys.ChatId);
            var sTimestamp = GetString(pushPayload, Constants.Notification.MessageDataKeys.Timestamp);
            var chatTitle = GetString(pushPayload, "chatTitle").NullIfEmpty() ?? ChannelName;
            var chatId = ChatId.TryParse(chatSid, allowNull: true);
            if (chatId is not { } vChatId || !long.TryParse(sTimestamp, out var epochMs)) {
                Log.LogWarning("Invalid PTT push payload");
                return PTPushResult.Create(new PTParticipant(ChannelName, null!));
            }

            _pendingWake = new PendingWake(vChatId, new Moment(epochMs * 10_000));
            return PTPushResult.Create(new PTParticipant(chatTitle, null!));
        }

        public override void DidActivateAudioSession(PTChannelManager channelManager, AVAudioSession audioSession)
        {
            Log.LogInformation("PTT audio session activated");
            OnAudioSessionActivated();
        }

        public override void DidDeactivateAudioSession(PTChannelManager channelManager, AVAudioSession audioSession)
        {
            Log.LogInformation("PTT audio session deactivated");
            AudioSession.IsExternallyActivated = false;
        }

        private static string? GetString(NSDictionary<NSString, NSObject> dict, string key)
            => dict[new NSString(key)]?.ToString();
    }

    private sealed class RestorationDelegate : PTChannelRestorationDelegate
    {
        public override PTChannelDescriptor Create(NSUuid channelUuid)
            => NewDescriptor();
    }
}
