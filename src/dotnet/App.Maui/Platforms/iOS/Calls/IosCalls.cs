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
    private readonly CXCallController _callController = new();
    private readonly ConcurrentDictionary<Guid, Call> _calls = new();
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
        var handle = conversationId?.ChatId.Value.NullIfEmpty() ?? callerName.NullIfEmpty() ?? FallbackCallerName;
        var update = new CXCallUpdate {
            RemoteHandle = new CXHandle(CXHandleType.Generic, handle),
            LocalizedCallerName = callerName.NullIfEmpty() ?? FallbackCallerName,
            HasVideo = hasVideo,
        };
        if (conversationId is null) {
            ReportUnroutableCall(update, completion);
            return;
        }

        var callId = CallId.For(conversationId);
        if (!_calls.TryAdd(callId, new Call(conversationId))) {
            // CallKit rejects a second report for a UUID it already holds, and a rejected report
            // is exactly what costs the app its VoIP delivery.
            DebugLog?.LogInformation("ReportIncomingCall: {ConversationId} is already reported", conversationId);
            completion();
            return;
        }

        _provider.ReportNewIncomingCall(new NSUuid(callId.ToString()), update, error => {
            if (error.ToException() is { } exc) {
                // Untracked again, or ListActiveCallChatIds hands SyncRings a phantom ring.
                _calls.TryRemove(callId, out _);
                Log.LogError(exc, "Failed to report incoming call {ConversationId}", conversationId);
            }
            completion();
        });
        // The ring itself is CallKit's from here; IncomingCallUI still needs to know so its
        // reactive state can end it.
        _ = DispatchToBlazor(
            c => c.GetRequiredService<IncomingCallUI>().OnRing(conversationId.ChatId),
            "ReportIncomingCall");
    }

    // The in-app UI answered this chat's call; mirror that into CallKit. Returns whether CallKit
    // still holds a call for the chat - i.e. whether the caller has to watch for its end.
    public bool AnswerCall(ChatId chatId)
    {
        if (!TryGetCall(chatId, out var callId, out var call))
            return false;

        // Answered before the pending flag is dropped: between the two, EndRingingCalls must
        // never see a call that is neither.
        var isAnswered = call.IsAnswered;
        call.MarkAnswered();
        call.ClearVerdictPending();
        if (isAnswered)
            return true;

        RequestTransaction(callId, call, new CXAnswerCallAction(new NSUuid(callId.ToString())), LocalAction.Answer);
        return true;
    }

    public void DeclineCall(ChatId chatId)
    {
        if (!TryGetCall(chatId, out var callId, out var call) || call.IsAnswered)
            return;

        // A local decline is the user ending the call, which CallKit takes as a transaction
        // rather than a report.
        RequestTransaction(callId, call, new CXEndCallAction(new NSUuid(callId.ToString())), LocalAction.End);
    }

    // The ring bookkeeping says only "this ring is over", never whether it was accepted - the
    // verdict follows in OnCallHandled. Holds EndRingingCalls off until it arrives.
    public void MarkRingHandledLocally(ChatId chatId)
    {
        if (TryGetCall(chatId, out _, out var call))
            call.MarkVerdictPending();
    }

    public void EndRingingCalls()
    {
        foreach (var (callId, call) in _calls) {
            if (!call.IsAnswered && !call.IsVerdictPending)
                EndCall(callId, CXCallEndedReason.RemoteEnded);
        }
    }

    public void EndCall(ConversationId conversationId)
        => EndCall(CallId.For(conversationId), CXCallEndedReason.RemoteEnded);

    public void EndCall(ChatId chatId)
        => EndCalls(chatId, CXCallEndedReason.RemoteEnded);

    public void FailCall(ChatId chatId)
        => EndCalls(chatId, CXCallEndedReason.Failed);

    public ChatId[] ListActiveCallChatIds()
        // Rings only: SyncRings turns these into OnRing, and a call the user already answered
        // is not a ring.
        => _calls.Values
            .Where(x => !x.IsAnswered)
            .Select(x => x.ConversationId.ChatId)
            .Distinct()
            .ToArray();

    // Calls whose end nothing is watching for yet - a scope handoff mid-call leaves these behind,
    // and neither EndRingingCalls nor CallKit itself would ever take them down.
    public ChatId[] ListCallsNeedingWatch()
        => _calls.Values
            .Where(x => x.IsAnswered || x.IsVerdictPending)
            .Select(x => x.ConversationId.ChatId)
            .Distinct()
            .ToArray();

    // CXProviderDelegate

    public override void DidReset(CXProvider provider)
    {
        DebugLog?.LogInformation("DidReset: dropping {Count} call(s)", _calls.Count);
        _calls.Clear();
        ReleaseAudioSession();
    }

    public override void PerformAnswerCallAction(CXProvider provider, CXAnswerCallAction action)
    {
        if (!TryGetCall(action.CallUuid, out var call)) {
            // Nothing to join: failing the action is what makes CallKit take the screen down.
            action.Fail();
            return;
        }

        var isHandledLocally = call.TryTakeLocalAction(LocalAction.Answer);
        call.MarkAnswered();
        // Only fulfilled here - the audio starts in DidActivateAudioSession, which is when
        // CallKit hands the session over. Activating it now would race the framework.
        AudioSession.SetOwner(AudioSessionOwner.CallKit);
        action.Fulfill();
        if (isHandledLocally)
            return;

        _ = DispatchToBlazor(
            c => c.GetRequiredService<IncomingCallUI>().Accept(call.ConversationId.ChatId),
            "PerformAnswerCallAction");
    }

    public override void PerformEndCallAction(CXProvider provider, CXEndCallAction action)
    {
        // Fulfilled even for a call we no longer track: ending is exactly what was asked, and a
        // failed end leaves the call up in the system UI.
        if (!TryRemoveCall(action.CallUuid, out var call)) {
            action.Fulfill();
            return;
        }

        var isHandledLocally = call.TryTakeLocalAction(LocalAction.End);
        action.Fulfill();
        if (isHandledLocally)
            return;

        var chatId = call.ConversationId.ChatId;
        var isAnswered = call.IsAnswered;
        _ = DispatchToBlazor(
            // Decline rejects a ring; a call the user already joined is a hang-up instead.
            c => isAnswered
                ? c.GetRequiredService<IncomingCallUI>().HangUp(chatId)
                : c.GetRequiredService<IncomingCallUI>().Decline(chatId),
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
        ReleaseAudioSession();
    }

    // Private methods

    private void ReportUnroutableCall(CXCallUpdate update, Action completion)
    {
        // A push that reports no call costs the app its VoIP delivery, so a payload we can't route
        // is still reported - and then failed right away, since nothing here could ever end it.
        var callUuid = new NSUuid(Guid.NewGuid().ToString());
        _provider.ReportNewIncomingCall(callUuid, update, error => {
            if (error.ToException() is { } exc)
                Log.LogError(exc, "Failed to report an unroutable incoming call");
            else
                _provider.ReportCall(callUuid, null, CXCallEndedReason.Failed);
            completion();
        });
    }

    private void EndCalls(ChatId chatId, CXCallEndedReason reason)
    {
        foreach (var (callId, call) in _calls) {
            if (call.ConversationId.ChatId == chatId)
                EndCall(callId, reason);
        }
    }

    private void EndCall(Guid callId, CXCallEndedReason reason)
    {
        if (!_calls.TryRemove(callId, out _))
            return;

        _provider.ReportCall(new NSUuid(callId.ToString()), null, reason);
    }

    private void RequestTransaction(Guid callId, Call call, CXAction action, LocalAction localAction)
    {
        // Armed before the request, or the Perform*Action it triggers would dispatch back into
        // IncomingCallUI for something the app itself just did.
        call.MarkLocalAction(localAction);
        _callController.RequestTransaction(new CXTransaction(action), error => {
            if (error.ToException() is not { } exc)
                return;

            // A marker left armed would make the user's next End press read as the app's own
            // doing, and an answer or decline CallKit refused must not leave a live system call.
            Log.LogError(exc, "CallKit {Action} transaction failed", localAction);
            call.ClearLocalAction();
            EndCall(callId, CXCallEndedReason.Failed);
        });
    }

    private static void ReleaseAudioSession()
    {
        // Only what we own: a CallKit deactivation must not take the session away from PTT.
        if (AudioSession.Owner == AudioSessionOwner.CallKit)
            AudioSession.ReleaseOwner(AudioSessionRelease.CallEnded);
    }

    private bool TryGetCall(ChatId chatId, out Guid callId, [NotNullWhen(true)] out Call? call)
    {
        foreach (var (key, value) in _calls) {
            if (value.ConversationId.ChatId == chatId) {
                (callId, call) = (key, value);
                return true;
            }
        }

        (callId, call) = (default, null);
        return false;
    }

    private bool TryGetCall(NSUuid callUuid, [NotNullWhen(true)] out Call? call)
    {
        call = null;
        return Guid.TryParse(callUuid.AsString(), out var callId) && _calls.TryGetValue(callId, out call);
    }

    private bool TryRemoveCall(NSUuid callUuid, [NotNullWhen(true)] out Call? call)
    {
        call = null;
        return Guid.TryParse(callUuid.AsString(), out var callId) && _calls.TryRemove(callId, out call);
    }

    // Nested types

    // What the app asked CallKit to do, so the Perform*Action it triggers can tell its own
    // request apart from the user pressing the same button.
    private enum LocalAction
    {
        None = 0,
        Answer,
        End,
    }

    private sealed class Call(ConversationId conversationId)
    {
        private int _isAnswered;
        private int _isVerdictPending;
        private int _localAction;

        public ConversationId ConversationId { get; } = conversationId;
        public bool IsAnswered => Volatile.Read(ref _isAnswered) != 0;
        public bool IsVerdictPending => Volatile.Read(ref _isVerdictPending) != 0;

        public void MarkAnswered()
            => Volatile.Write(ref _isAnswered, 1);
        public void MarkVerdictPending()
            => Volatile.Write(ref _isVerdictPending, 1);
        public void ClearVerdictPending()
            => Volatile.Write(ref _isVerdictPending, 0);
        public void MarkLocalAction(LocalAction action)
            => Volatile.Write(ref _localAction, (int)action);
        public void ClearLocalAction()
            => Volatile.Write(ref _localAction, (int)LocalAction.None);
        public bool TryTakeLocalAction(LocalAction action)
            => Interlocked.CompareExchange(ref _localAction, (int)LocalAction.None, (int)action) == (int)action;
    }
}
