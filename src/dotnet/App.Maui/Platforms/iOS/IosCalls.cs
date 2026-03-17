using ActualChat.App.Maui.Audio;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using CallKit;
using Foundation;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

public class IosCalls : CXProviderDelegate
{
    public static IosCalls Instance { get; } = new();

    private readonly CXProvider _provider;
    private readonly CXCallController _callController = new();

    private ILogger Log => field ??= StaticLog.For<IosCalls>();
    private readonly LruCache<NSUuid, ChatId> _chatIdByCallId = new (10);
    private NSUuid? _activeCallId;

    private IosCalls()
    {
        var config = new CXProviderConfiguration();
        config.SupportsVideo = true;
        config.MaximumCallsPerCallGroup = 1;
        config.SupportedHandleTypes =
            new NSSet<NSNumber>((int)CXHandleType.PhoneNumber, (int)CXHandleType.EmailAddress);
        _provider = new CXProvider(config);
        _provider.SetDelegate(this, null);
    }

    public void ReportIncomingCall(
        NSUuid uuid,
        ChatId chatId,
        Phone? phone,
        string? email,
        string displayName,
        bool hasVideo,
        Action completion)
    {
        var handle = phone != null
                ? new CXHandle(CXHandleType.PhoneNumber, phone.E164Value)
                : !email.IsNullOrEmpty()
                    ? new CXHandle(CXHandleType.EmailAddress, email)
                    : new CXHandle(CXHandleType.Generic, displayName);
        var update = new CXCallUpdate
        {
            RemoteHandle = handle,
            LocalizedCallerName = displayName,
            HasVideo = hasVideo,
        };

        _chatIdByCallId[uuid] = chatId;
        _provider.ReportNewIncomingCall(uuid, update, error => {
            if (error.ToException() is { } exc)
                Log.LogError(exc, "Failed to report incoming call");
            completion();
        });
    }

    public void StartOutgoingCall(string handle, bool hasVideo)
    {
        var handleObj = new CXHandle(CXHandleType.PhoneNumber, handle);
        var action = new CXStartCallAction(new NSUuid(), handleObj) { Video = hasVideo };
        var transaction = new CXTransaction(action);
        Request(transaction);
    }

    public void EndCall(NSUuid uuid)
    {
        var action = new CXEndCallAction(uuid);
        Request(new CXTransaction(action));
    }

    private void Request(CXTransaction transaction)
        => _callController.RequestTransaction(transaction, error =>
        {
            if (error.ToException() is { } exc)
                Log.LogError(exc, "Failed to report incoming call");
        });

    public override void DidReset(CXProvider provider)
    {
        // Stop audio, clean up VoIP connection, etc.
    }

    public override void PerformAnswerCallAction(CXProvider provider, CXAnswerCallAction action)
    {
        var callId = action.CallUuid;
        _activeCallId = action.CallUuid;
        _ = DispatchToBlazor(async c => {
            if (!_chatIdByCallId.TryGetValue(callId, out var chatId)) {
                Log.LogError("DidActivateAudioSession: unable to find chatId for call #{CallId}", callId);
                return;
            }
            var audioSession = c.GetRequiredService<AudioSession>();
            // NOTE: Only configuring the session here, not starting it to avoid errors.
            // CallKit activates the audio session and notifies us via DidActivateAudioSession.
            await audioSession.Reconfigure(AudioFocusMode.Recording, false).ConfigureAwait(false);
            action.Fulfill();
        });
        Log.LogInformation("PerformAnswerCallAction: answered call #{CallId}", action.CallUuid);
    }

    public override void DidActivateAudioSession(CXProvider provider, AVAudioSession audioSession)
    {
        if (_activeCallId is not { } callId) {
            Log.LogError("DidActivateAudioSession called without active call");
            return;
        }
        if (!_chatIdByCallId.TryGetValue(callId, out var chatId)) {
            Log.LogError("DidActivateAudioSession: unable to find chatId for call #{CallId}", callId);
            return;
        }
        Log.LogInformation("DidActivateAudioSession: call #{CallId}, chat #{ChatId}", callId, chatId);
        _ = DispatchToBlazor(async c => {
            var chatAudioUI = c.GetRequiredService<ChatAudioUI>();
            await chatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(false);
        });
    }

    public override void PerformEndCallAction(CXProvider provider, CXEndCallAction action)
    {
        _ = DispatchToBlazor(async c => {
            var chatAudioUI = c.GetRequiredService<ChatAudioUI>();
            await chatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
            _chatIdByCallId.Remove(action.CallUuid);
            action.Fulfill();
        });
    }

    public override void PerformStartCallAction(CXProvider provider, CXStartCallAction action)
    {
        provider.ReportConnectingOutgoingCall(action.CallUuid, null);
        provider.ReportConnectedOutgoingCall(action.CallUuid, NSDate.Now);
        action.Fulfill();
    }
}
