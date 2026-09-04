using ActualChat.App.Maui.Audio;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using AVFoundation;
using CallKit;
using Foundation;

namespace ActualChat.App.Maui;

/// <summary>
/// The app's CallKit provider: reports rings from VoIP pushes and routes the system
/// call UI's actions back into <see cref="IncomingCallUI"/>. A static singleton because
/// a VoIP push routinely starts the process, with no Blazor scope to belong to.
/// </summary>
public class IosCalls : CXProviderDelegate
{
    private const string FallbackCallerName = "Voxt";

    public static IosCalls Instance { get; } = new();

    private readonly CXProvider _provider;
    private readonly ConcurrentDictionary<Guid, ConversationId?> _conversationIdByCallId = new();
    private ILogger Log => field ??= StaticLog.For<IosCalls>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.IosCalls);

    private IosCalls()
    {
        var config = new CXProviderConfiguration {
            SupportsVideo = true,
            MaximumCallsPerCallGroup = 1,
            // Generic, not PhoneNumber/EmailAddress: a chat identity is neither, and the
            // typed handles put a contact detail into the system call log.
            SupportedHandleTypes = new NSSet<NSNumber>((int)CXHandleType.Generic),
        };
        _provider = new CXProvider(config);
        _provider.SetDelegate(this, null);
    }

    public void ReportIncomingCall(
        ConversationId? conversationId, string callerName, bool hasVideo, Action completion)
    {
        // An unparseable payload still has to ring, on a fresh call id: a VoIP push that reports
        // no call costs the app its VoIP delivery.
        var callId = conversationId is null ? Guid.NewGuid() : CallId.For(conversationId);
        _conversationIdByCallId[callId] = conversationId;
        var handle = conversationId?.ChatId.Value.NullIfEmpty() ?? callerName.NullIfEmpty() ?? FallbackCallerName;
        var update = new CXCallUpdate {
            RemoteHandle = new CXHandle(CXHandleType.Generic, handle),
            LocalizedCallerName = callerName.NullIfEmpty() ?? FallbackCallerName,
            HasVideo = hasVideo,
        };
        _provider.ReportNewIncomingCall(new NSUuid(callId.ToString()), update, error => {
            if (error.ToException() is { } exc)
                Log.LogError(exc, "Failed to report incoming call {ConversationId}", conversationId);
            completion();
        });
        if (conversationId is null)
            return;

        // The ring itself is CallKit's from here; IncomingCallUI still needs to know so its
        // reactive state can end it.
        _ = DispatchToBlazor(
            c => c.GetRequiredService<IncomingCallUI>().OnRing(conversationId.ChatId),
            "ReportIncomingCall");
    }

    public void EndCall(ConversationId conversationId)
        => EndCall(CallId.For(conversationId));

    public ChatId[] ListActiveCallChatIds()
        => _conversationIdByCallId.Values
            .SkipNullItems()
            .Select(x => x.ChatId)
            .Distinct()
            .ToArray();

    // CXProviderDelegate

    public override void DidReset(CXProvider provider)
    {
        DebugLog?.LogInformation("DidReset: dropping {Count} call(s)", _conversationIdByCallId.Count);
        _conversationIdByCallId.Clear();
        AudioSession.ReleaseOwner(AudioSessionRelease.CallEnded);
    }

    public override void PerformAnswerCallAction(CXProvider provider, CXAnswerCallAction action)
    {
        if (!TryGetConversationId(action.CallUuid, out var conversationId)) {
            // Nothing to join: failing the action is what makes CallKit take the screen down.
            action.Fail();
            return;
        }

        // Only fulfilled here - the audio starts in DidActivateAudioSession, which is when
        // CallKit hands the session over. Activating it now would race the framework.
        AudioSession.SetOwner(AudioSessionOwner.CallKit);
        action.Fulfill();
        _ = DispatchToBlazor(
            c => c.GetRequiredService<IncomingCallUI>().Accept(conversationId.ChatId),
            "PerformAnswerCallAction");
    }

    public override void PerformEndCallAction(CXProvider provider, CXEndCallAction action)
    {
        if (!TryGetConversationId(action.CallUuid, out var conversationId)) {
            // Fulfilled, not failed: ending is exactly what was asked, and a failed end leaves
            // the call up in the system UI.
            action.Fulfill();
            return;
        }

        _conversationIdByCallId.TryRemove(CallId.For(conversationId), out _);
        action.Fulfill();
        _ = DispatchToBlazor(
            // Decline covers both a rejected ring and a hang-up: LiveSessionUI decides
            // which by whether we already joined.
            c => c.GetRequiredService<IncomingCallUI>().Decline(conversationId.ChatId),
            "PerformEndCallAction");
    }

    public override void PerformStartCallAction(CXProvider provider, CXStartCallAction action)
    {
        provider.ReportConnectingOutgoingCall(action.CallUuid, null);
        action.Fulfill();
    }

    public override void DidActivateAudioSession(CXProvider provider, AVAudioSession audioSession)
    {
        DebugLog?.LogInformation("DidActivateAudioSession");
        AudioSession.SetOwner(AudioSessionOwner.CallKit);
    }

    public override void DidDeactivateAudioSession(CXProvider provider, AVAudioSession audioSession)
    {
        DebugLog?.LogInformation("DidDeactivateAudioSession");
        AudioSession.ReleaseOwner(AudioSessionRelease.CallEnded);
    }

    // Private methods

    private void EndCall(Guid callId)
    {
        if (!_conversationIdByCallId.TryRemove(callId, out _))
            return;

        _provider.ReportCall(new NSUuid(callId.ToString()), null, CXCallEndedReason.RemoteEnded);
    }

    private bool TryGetConversationId(NSUuid callUuid, [NotNullWhen(true)] out ConversationId? conversationId)
    {
        conversationId = null;
        if (!Guid.TryParse(callUuid.AsString(), out var callId))
            return false;

        return _conversationIdByCallId.TryGetValue(callId, out conversationId) && conversationId is not null;
    }
}
