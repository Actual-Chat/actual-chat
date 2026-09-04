using ActualChat.App.Maui.Audio;
using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using AVFoundation;
using CallKit;
using Foundation;

namespace ActualChat.App.Maui;

/// <summary>
/// The app's CallKit provider: reports rings from VoIP pushes and outgoing calls the user
/// places, and routes the system call UI's actions back into <see cref="IncomingCallUI"/>
/// and <see cref="LiveSessionUI"/>. A static singleton because a VoIP push routinely starts
/// the process, with no Blazor scope to belong to.
/// </summary>
public class IosCalls : CXProviderDelegate
{
    private const string FallbackCallerName = "Voxt";
    // Nothing else takes down an outgoing call that never reached a verdict: the watch that reports
    // one dies with its Blazor scope, EndRingingCalls skips outgoing calls, and a CallKit call left up
    // holds the audio session away from the app until the user presses End. Anchored to the server's
    // own no-observer ring backstop, so it can never end a call that is still legitimately ringing.
    private static readonly TimeSpan OutgoingCallTimeout = Constants.Call.RingTtl + TimeSpan.FromSeconds(5);

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
        if (!_calls.TryAdd(callId, new Call(conversationId.ChatId, hasVideo))) {
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
        if (!TryGetCall(chatId, false, out var callId, out var call))
            return false;

        // Answered before the pending flag is dropped: between the two, EndRingingCalls must
        // never see a call that is neither.
        var isAnswered = call.IsAnswered;
        call.MarkAnswered();
        call.ClearVerdictPending();
        if (isAnswered)
            return true;

        AudioSession.IsCallVideo = call.HasVideo;
        RequestTransaction(callId, call, new CXAnswerCallAction(new NSUuid(callId.ToString())), LocalAction.Answer);
        return true;
    }

    public void DeclineCall(ChatId chatId)
    {
        if (!TryGetCall(chatId, false, out var callId, out var call) || call.IsAnswered)
            return;

        // A local decline is the user ending the call, which CallKit takes as a transaction
        // rather than a report.
        RequestTransaction(callId, call, new CXEndCallAction(new NSUuid(callId.ToString())), LocalAction.End);
    }

    public void EndAnsweredCall(ChatId chatId)
    {
        // An answer that never became a call: EndRingingCalls skips answered calls and no end
        // watch was started, so nothing else would ever take this one down.
        if (!TryGetCall(chatId, false, out var callId, out var call) || !call.IsAnswered)
            return;

        // A transaction rather than a report, exactly as a decline is; the marker keeps
        // PerformEndCallAction from dispatching a hang-up back into a join that already failed.
        RequestTransaction(callId, call, new CXEndCallAction(new NSUuid(callId.ToString())), LocalAction.End);
    }

    public void MarkRingHandledLocally(ChatId chatId)
    {
        // The ring bookkeeping says only "this ring is over", never whether it was accepted - the
        // verdict follows in OnCallHandled. Holds EndRingingCalls off until it arrives.
        if (TryGetCall(chatId, false, out _, out var call))
            call.MarkVerdictPending();
    }

    public void EndRingingCalls()
    {
        // Rings only - an outgoing call has no ring to end, and the ring bookkeeping fires this
        // for every incoming call the app resolves.
        foreach (var (callId, call) in _calls) {
            if (IsRinging(call))
                EndCall(callId, CXCallEndedReason.RemoteEnded);
        }
    }

    public void EndRingingCall(ChatId chatId)
    {
        // Ringing only: the server dismisses the ring for the accept too, and ending an answered
        // call here would take the live CallKit call down seconds after the user answered.
        foreach (var (callId, call) in _calls) {
            if (call.ChatId == chatId && IsRinging(call))
                EndCall(callId, CXCallEndedReason.RemoteEnded);
        }
    }

    public void EndCall(ChatId chatId)
        => EndCalls(chatId, CXCallEndedReason.RemoteEnded);

    public void FailCall(ChatId chatId)
        => EndCalls(chatId, CXCallEndedReason.Failed);

    // Outgoing calls are keyed by a random id rather than CallId.For: the conversation this call
    // creates doesn't exist yet, and CallKit needs the UUID now. Synchronous, so nothing can report
    // a status for a call this map doesn't hold yet; the callee's name follows in SetOutgoingCallName.
    public void StartOutgoingCall(ChatId chatId, bool hasVideo)
    {
        if (TryGetCall(chatId, true, out _, out _)) {
            DebugLog?.LogInformation("StartOutgoingCall: #{ChatId} is already dialing", chatId);
            return;
        }

        var callId = Guid.NewGuid();
        var call = new Call(chatId, hasVideo, true);
        _calls[callId] = call;
        var handle = new CXHandle(CXHandleType.Generic, chatId.Value);
        var action = new CXStartCallAction(new NSUuid(callId.ToString()), handle) {
            Video = hasVideo,
            ContactIdentifier = FallbackCallerName,
        };
        // No local-action marker: PerformStartCallAction is never the user pressing anything, so
        // there is nothing for it to dispatch back into the app.
        RequestTransaction(callId, call, action, LocalAction.None);
        _ = BackgroundTask.Run(
            () => EndStaleOutgoingCall(callId),
            Log, $"Outgoing call backstop failed for chat #{chatId}");
    }

    public void SetOutgoingCallName(ChatId chatId, string calleeName)
    {
        if (calleeName.IsNullOrEmpty() || !TryGetCall(chatId, true, out var callId, out _))
            return;

        var update = new CXCallUpdate { LocalizedCallerName = calleeName };
        _provider.ReportCall(new NSUuid(callId.ToString()), update);
    }

    // Returns whether CallKit still holds the call - i.e. whether its end has to be watched for,
    // which is only the case once the callee has answered.
    public bool ReportOutgoingCallStatus(ChatId chatId, CallStatus status)
    {
        if (!TryGetCall(chatId, true, out var callId, out var call))
            return false;

        switch (status) {
        case CallStatus.Accepted:
            call.MarkAnswered();
            // The caller is the one who never got this from an answer action, and an outgoing video
            // call would otherwise start on the receiver-first audio route.
            AudioSession.IsCallVideo = call.HasVideo;
            _provider.ReportConnectedOutgoingCall(new NSUuid(callId.ToString()), NSDate.Now);
            return true;
        case CallStatus.Declined:
            // Not DeclinedElsewhere: that one means this user declined on another device.
            EndCall(callId, CXCallEndedReason.RemoteEnded);
            return false;
        case CallStatus.NoAnswer:
            EndCall(callId, CXCallEndedReason.Unanswered);
            return false;
        default:
            // Ignored rather than ended: Dialing is not an outcome, and ending a live call on one
            // would be far worse than leaving the backstop to it.
            Log.LogWarning("ReportOutgoingCallStatus: unexpected {Status} for chat #{ChatId}", status, chatId);
            return false;
        }
    }

    public void CancelOutgoingCall(ChatId chatId)
    {
        if (!TryGetCall(chatId, true, out var callId, out var call))
            return;

        // The user ending their own call, which CallKit takes as a transaction rather than a report.
        RequestTransaction(callId, call, new CXEndCallAction(new NSUuid(callId.ToString())), LocalAction.End);
    }

    public ChatId[] ListActiveCallChatIds()
        // Rings only: SyncRings turns these into OnRing, and a call the user already answered
        // is not a ring.
        => _calls.Values
            .Where(x => !x.IsOutgoing && !x.IsAnswered)
            .Select(x => x.ChatId)
            .Distinct()
            .ToArray();

    // Calls whose end nothing is watching for yet - a scope handoff mid-call leaves these behind,
    // and neither EndRingingCalls nor CallKit itself would ever take them down.
    public ChatId[] ListCallsNeedingWatch()
        => _calls.Values
            .Where(x => x.IsAnswered || x.IsVerdictPending)
            .Select(x => x.ChatId)
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
        AudioSession.IsCallVideo = call.HasVideo;
        // Only fulfilled here - the audio starts in DidActivateAudioSession, which is when
        // CallKit hands the session over. Activating it now would race the framework.
        AudioSession.SetOwner(AudioSessionOwner.CallKit);
        action.Fulfill();
        if (isHandledLocally)
            return;

        _ = DispatchToBlazor(
            c => c.GetRequiredService<IncomingCallUI>().Accept(call.ChatId),
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

        var isAnswered = call.IsAnswered;
        if (isAnswered)
            AudioSession.IsCallVideo = false;
        var isHandledLocally = call.TryTakeLocalAction(LocalAction.End);
        action.Fulfill();
        if (isHandledLocally)
            return;

        var chatId = call.ChatId;
        var isDialingOut = call.IsOutgoing && !isAnswered;
        _ = DispatchToBlazor(c => EndCallLocally(c, chatId, isAnswered, isDialingOut), "PerformEndCallAction");
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

    private static Task EndCallLocally(IServiceProvider services, ChatId chatId, bool isAnswered, bool isDialingOut)
    {
        // The caller taking back a call nobody picked up yet is a cancel, not a decline.
        if (isDialingOut)
            return services.GetRequiredService<LiveSessionUI>().CancelCall(chatId, CancellationToken.None);

        // Decline rejects a ring; a call the user already joined is a hang-up instead.
        return isAnswered
            ? services.GetRequiredService<IncomingCallUI>().HangUp(chatId)
            : services.GetRequiredService<IncomingCallUI>().Decline(chatId);
    }

    private async Task EndStaleOutgoingCall(Guid callId)
    {
        await Task.Delay(OutgoingCallTimeout).ConfigureAwait(false);
        // Keyed by the call's own id, so a later call to the same chat outlives an earlier deadline.
        if (_calls.TryGetValue(callId, out var call) && call is { IsOutgoing: true, IsAnswered: false }) {
            Log.LogWarning("Ending an outgoing call to #{ChatId} nothing reported on", call.ChatId);
            EndCall(callId, CXCallEndedReason.Unanswered);
        }
    }

    private void EndCalls(ChatId chatId, CXCallEndedReason reason)
    {
        foreach (var (callId, call) in _calls) {
            if (call.ChatId == chatId)
                EndCall(callId, reason);
        }
    }

    private void EndCall(Guid callId, CXCallEndedReason reason)
    {
        if (!_calls.TryRemove(callId, out var call))
            return;

        if (call.IsAnswered)
            AudioSession.IsCallVideo = false;
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
            // doing, and an action CallKit refused must not leave a live system call.
            Log.LogError(exc, "CallKit {Action} transaction failed", action.GetType().Name);
            call.ClearLocalAction();
            EndCall(callId, CXCallEndedReason.Failed);
        });
    }

    private static void ReleaseAudioSession()
    {
        // Only the ownership is conditional - a CallKit deactivation must not take the session
        // away from PTT, but the route latch is this call's and PTT may have taken it mid-call.
        if (AudioSession.Owner == AudioSessionOwner.CallKit)
            AudioSession.ReleaseOwner(AudioSessionRelease.CallEnded);
        AudioSession.ResetCallRouteLatch();
        AudioSession.IsCallVideo = false;
    }

    private bool TryGetCall(ChatId chatId, bool isOutgoing, out Guid callId, [NotNullWhen(true)] out Call? call)
    {
        foreach (var (key, value) in _calls) {
            if (value.ChatId == chatId && value.IsOutgoing == isOutgoing) {
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

    private static bool IsRinging(Call call)
        => !call.IsOutgoing && !call.IsAnswered && !call.IsVerdictPending;

    // Nested types

    // What the app asked CallKit to do, so the Perform*Action it triggers can tell its own
    // request apart from the user pressing the same button.
    private enum LocalAction
    {
        None = 0,
        Answer,
        End,
    }

    private sealed class Call(ChatId chatId, bool hasVideo, bool isOutgoing = false)
    {
        private int _isAnswered;
        private int _isVerdictPending;
        private int _localAction;

        public ChatId ChatId { get; } = chatId;
        public bool HasVideo { get; } = hasVideo;
        public bool IsOutgoing { get; } = isOutgoing;
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
