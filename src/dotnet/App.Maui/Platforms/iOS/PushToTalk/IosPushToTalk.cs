using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services;
using ActualChat.Notifications;
using ActualChat.Security;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;
using PushToTalk;
using UIKit;
using DeviceType = ActualChat.Notifications.DeviceType;

namespace ActualChat.App.Maui;

/// <summary>
/// Process-level Apple Push to Talk integration: one aggregate "Voxt" channel whose join
/// survives app kill/reboot; incoming PTT pushes route into <see cref="WalkieTalkieSession"/>.
/// Transmission mode follows the user's Push-to-Talk-reply setting.
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
    private static Transmission? _transmission;
    private static int _isTransmitEnabled;
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

    public static void SetTransmitEnabled(bool isEnabled)
    {
        Volatile.Write(ref _isTransmitEnabled, isEnabled ? 1 : 0);
        var manager = _manager;
        if (manager?.ActiveChannelUuid is null)
            return;

        ApplyTransmissionMode(manager, ChannelUuid, isEnabled);
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

    private static void ApplyTransmissionMode(PTChannelManager manager, NSUuid channelUuid, bool isEnabled)
    {
        // Off must mean ListenOnly, not an inert button: a Talk press that silently does nothing
        // is worse than no Talk button at all.
        var mode = isEnabled ? PTTransmissionMode.FullDuplex : PTTransmissionMode.ListenOnly;
        manager.SetTransmissionMode(mode, channelUuid, error => {
            if (error is not null)
                Log.LogWarning("SetTransmissionMode({Mode}) failed: {Error}", mode, error.LocalizedDescription);
        });
    }

    private static void SetDescriptorTitle(string chatTitle)
    {
        var manager = _manager;
        if (manager?.ActiveChannelUuid is null)
            return;

        // The channel is the aggregate "Voxt", so without this the system sheet cannot tell the
        // user which chat a Talk press would reach.
        manager.SetChannelDescriptor(new PTChannelDescriptor(chatTitle, UIImage.FromBundle("AppIcon")),
            ChannelUuid,
            error => {
                if (error is not null)
                    Log.LogWarning("SetChannelDescriptor failed: {Error}", error.LocalizedDescription);
            });
    }

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

    private static void OnTransmitBegan()
    {
        BlazorWebViewApp.EnsureStarted();
        lock (Lock)
            _transmission = new Transmission { CreatedAt = CpuTimestamp.Now };
    }

    private static void StartTransmitReply(Transmission transmission)
    {
        var preRollToken = PttPreRoll.Start();
        bool isCurrent;
        lock (Lock) {
            isCurrent = ReferenceEquals(_transmission, transmission);
            if (isCurrent) {
                transmission.PreRollToken = preRollToken;
                transmission.IsStarted = true;
            }
        }

        if (!isCurrent) {
            // The pre-roll engine already holds the hardware input node, and only a Discard takes
            // it back off - a second engine must never end up on that node.
            PttPreRoll.Discard(preRollToken);
            return;
        }

        _ = BackgroundTask.Run(async () => {
            var reply = await WalkieTalkieSession.HandleTransmit(IosPlatform.Instance)
                .ConfigureAwait(false);
            bool isEndPending;
            lock (Lock) {
                transmission.Reply = reply;
                isCurrent = ReferenceEquals(_transmission, transmission);
                isEndPending = transmission.IsEndPending;
            }

            if (!isCurrent) {
                // Superseded: this transmission owns nothing now, but its pre-roll engine is still
                // on the input node and the reply it opened still has to close.
                await StopTransmitReply(transmission).ConfigureAwait(false);
                return;
            }

            if (reply is null) {
                // StopTransmitReply, not a bare Discard: it also clears _transmission, and a
                // latched one would make the next incoming wake open the mic instead of playing.
                await StopTransmitReply(transmission).ConfigureAwait(false);
                StopTransmitting();
                return;
            }

            if (isEndPending) {
                // The user let go before the app finished booting. The buffered words are real
                // speech, so the reply still goes out - it just holds open long enough for
                // AppleAudioCapture to drain the pre-roll into the encoder.
                await Task.Delay(Constants.Audio.WalkieTalkiePreRollFlushDelay).ConfigureAwait(false);
                await StopTransmitReply(transmission).ConfigureAwait(false);
            }
        }, Log, "PTT transmit reply failed", CancellationToken.None);
    }

    private static void OnChannelLeft()
    {
        OnTransmitEnded();
        lock (Lock)
            // A transmission that never reached StartTransmitReply has no reply task left to
            // finish it, and no further callback will arrive - so drop it here rather than let it
            // latch. A started one is left alone: its reply task owns the mic and the clear.
            if (_transmission is { IsStarted: false })
                _transmission = null;
    }

    private static void OnTransmitEnded()
    {
        Transmission? transmission;
        lock (Lock) {
            transmission = _transmission;
            if (transmission is null)
                return;

            if (transmission.Reply is null) {
                transmission.IsEndPending = true;
                AudioSession.ReleaseOwner(AudioSessionRelease.TransmitEnded);
                return;
            }
        }
        AudioSession.ReleaseOwner(AudioSessionRelease.TransmitEnded);
        _ = BackgroundTask.Run(
            () => StopTransmitReply(transmission),
            Log, "Stopping the PTT transmit reply failed", CancellationToken.None);
    }

    private static async Task StopTransmitReply(Transmission transmission)
    {
        WalkieTalkieReply? reply;
        long preRollToken;
        lock (Lock) {
            (reply, preRollToken) = (transmission.Reply, transmission.PreRollToken);
            if (ReferenceEquals(_transmission, transmission))
                _transmission = null;
        }

        // Both cleanups run even for a superseded transmission: the engine it left on the input
        // node and the reply it opened are still its own, and nothing else will close them.
        PttPreRoll.Discard(preRollToken);
        if (reply is null || AppScopeAccessor.Current is not { } services)
            return;

        // Only stop what this transmission opened - StopReply(reply) no-ops once anything else
        // has replaced the open reply, so a gesture-held mic is never closed here.
        var hub = services.GetRequiredService<AppUIHub>();
        await hub.WalkieTalkieReplyUI.StopReply(reply).ConfigureAwait(false);
    }

    private static void StopTransmitting()
    {
        var manager = _manager;
        if (manager?.ActiveChannelUuid is null)
            return;

        manager.StopTransmitting(ChannelUuid);
    }

    private static void OnAudioSessionActivated()
    {
        Transmission? transmission;
        lock (Lock) {
            transmission = _transmission;
            if (transmission is not null && MustAbandon(transmission)) {
                _transmission = null;
                transmission = null;
            }
        }
        AudioSession.SetOwner(AudioSessionOwnership.OnActivated(transmission is not null));
        if (transmission is not null) {
            StartTransmitReply(transmission);
            return;
        }

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

    private static bool MustAbandon(Transmission transmission)
    {
        // Only a transmission that never reached StartTransmitReply: it has no reply task to
        // finish it, so acting on it at a later activation would take the session as PttTransmit,
        // open the mic and swallow the pending wake - on what is most likely an incoming message.
        // A started one is bounded by its own reply task instead.
        if (transmission.IsStarted)
            return false;

        return transmission.IsEndPending
            || transmission.CreatedAt.Elapsed > Constants.Audio.WalkieTalkiePttTransmitStartupTimeout;
    }

    // Nested types

    private sealed record PendingWake(ChatId ChatId, Moment StartedAt);

    private sealed class Transmission
    {
        public CpuTimestamp CreatedAt { get; init; }
        public long PreRollToken { get; set; }
        public bool IsStarted { get; set; }
        public WalkieTalkieReply? Reply { get; set; }
        public bool IsEndPending { get; set; }
    }

    private sealed class IosPlatform : WalkieTalkiePlatform
    {
        public static readonly IosPlatform Instance = new();

        public override void OnWakeFailed(ChatId chatId)
        {
            AudioSession.ReleaseOwner(AudioSessionRelease.ChannelLeft);
            ClearActiveParticipant();
        }

        public override void OnHeadlessTeardown()
        {
            AudioSession.ReleaseOwner(AudioSessionRelease.ChannelLeft);
            ClearActiveParticipant();
        }

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
            ApplyTransmissionMode(channelManager, channelUuid, Volatile.Read(ref _isTransmitEnabled) != 0);
        }

        public override void DidLeaveChannel(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelLeaveReason reason)
        {
            OnChannelLeft();
            // A leave can tear the session down without DidDeactivateAudioSession;
            // a stuck flag would permanently disable the app's own session activation.
            AudioSession.ReleaseOwner(AudioSessionRelease.ChannelLeft);
            Log.LogInformation("PTT channel left ({Reason})", reason);
            DeregisterToken();
        }

        public override void DidBeginTransmitting(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelTransmitRequestSource source)
        {
            Log.LogInformation("PTT transmit began ({Source})", source);
            OnTransmitBegan();
        }

        public override void DidEndTransmitting(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelTransmitRequestSource source)
        {
            Log.LogInformation("PTT transmit ended ({Source})", source);
            OnTransmitEnded();
        }

        public override void FailedToBeginTransmittingInChannel(
            PTChannelManager channelManager, NSUuid channelUuid, NSError error)
        {
            Log.LogWarning("PTT transmit was refused: {Error}", error.LocalizedDescription);
            OnTransmitEnded();
        }

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
            SetDescriptorTitle(chatTitle);
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
            AudioSession.ReleaseOwner(AudioSessionRelease.Deactivated);
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
