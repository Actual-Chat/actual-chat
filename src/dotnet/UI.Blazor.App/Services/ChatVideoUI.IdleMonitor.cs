using ActualChat.UI.Blazor.Resources;
namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    private static readonly TimeSpan IdleMonitorEpsilon = TimeSpan.FromMilliseconds(50);

    [ComputeMethod]
    protected virtual async Task<ChatId?> GetActiveVideoChatId(CancellationToken cancellationToken = default)
    {
        var rec = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (rec is not null)
            return rec;

        var screen = await _screenCastChatId.Use(cancellationToken).ConfigureAwait(false);
        if (screen is not null)
            return screen;

        return await _watchingChatId.Use(cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsOwnTranscribingInChat(
        ChatId chatId,
        AuthorId ownAuthorId,
        CancellationToken cancellationToken)
    {
        var idRange = await Hub.Chats
            .GetIdRange(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
        if (idRange.IsEmptyOrNegative) {
            Log.LogInformation("IdleMonitor[{ChatId}]: IsOwnTranscribingInChat — idRange empty", chatId);
            return false;
        }
        const int maxScan = 32;
        var startId = Math.Max(idRange.Start, idRange.End - maxScan);
        var reader = Hub.NewEntryReader(chatId);
        var entry = await reader.GetLast(
                (startId, idRange.End),
                e => e.AuthorId == ownAuthorId && e.IsContentStreaming,
                maxScan,
                cancellationToken)
            .ConfigureAwait(false);
        Log.LogInformation(
            "IdleMonitor[{ChatId}]: IsOwnTranscribingInChat — idRange=[{Start}..{End}), scan>={ScanStart}, found={EntryId} streaming={Streaming}",
            chatId, idRange.Start, idRange.End, startId,
            entry?.Id.ToString() ?? "<none>", entry?.IsContentStreaming);
        return entry is not null;
    }

    // Private methods

    private async Task MonitorVideoIdleness(CancellationToken cancellationToken)
    {
        var cpuClock = Clocks.CpuClock;
        var cActiveChat = await Computed
            .Capture(() => GetActiveVideoChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        ChatId? sessionChatId = null;
        var lastConfirmedAt = default(Moment);
        var lastVoiceActiveAt = default(Moment);
        var lastVadActiveAt = default(Moment);

        while (!cancellationToken.IsCancellationRequested) {
            var activeChatId = cActiveChat.Value;
            if (activeChatId is null) {
                sessionChatId = null;
                lastVoiceActiveAt = default;
                lastVadActiveAt = default;
                cActiveChat = await cActiveChat
                    .When(x => x is not null, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (sessionChatId != activeChatId) {
                sessionChatId = activeChatId;
                lastConfirmedAt = cpuClock.Now;
                lastVoiceActiveAt = default;
                lastVadActiveAt = default;
                Log.LogInformation("IdleMonitor: session started for {ChatId}", activeChatId);
            }

            // Inactivity timer applies only when the user is the sender
            // (recording or screencasting). Pure watching is not stopped here.
            var ownSourceKind = await GetOwnSourceKind(activeChatId, cancellationToken).ConfigureAwait(false);
            var hasOwnStream = ownSourceKind is not null;

            // VAD: fast, bursty. We latch the moment of the last hit and use
            // it as a "this device" anchor for the transcription check below.
            var cAudioRecorderState = Hub.AudioRecorder.State.Computed;
            var audioRecorderState = cAudioRecorderState.Value;
            var vadActiveHere = hasOwnStream
                && audioRecorderState.IsVoiceActive
                && audioRecorderState.ChatId == activeChatId;
            if (vadActiveHere)
                lastVadActiveAt = cpuClock.Now;

            // Transcription is the primary signal: a streaming own-author entry
            // means the user IS speaking somewhere. VAD recency anchors it to
            // THIS device — if they switched to another device, transcription
            // still fires here (same author), but local VAD goes stale → grace
            // expires → we stop bumping → this device's timer fires normally.
            Computed<bool>? cIsOwnTranscribing = null;
            if (hasOwnStream) {
                var ownAuthor = await Hub.AuthorUI
                    .GetOwn(activeChatId, cancellationToken)
                    .ConfigureAwait(false);
                var ownAuthorId = ownAuthor.Id;
                cIsOwnTranscribing = await Computed
                    .Capture(
                        () => IsOwnTranscribingInChat(activeChatId, ownAuthorId, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
                var isVadRecent = lastVadActiveAt != default
                    && cpuClock.Now - lastVadActiveAt <= Constants.Video.VadActiveGrace;
                Log.LogInformation(
                    "IdleMonitor[{ChatId}]: vadActiveHere={Vad}, lastVadActiveAt={LastVad}, isVadRecent={IsVadRecent}, isOwnTranscribing={IsOwnTranscribing}",
                    activeChatId, vadActiveHere, lastVadActiveAt, isVadRecent, cIsOwnTranscribing.Value);
                if (cIsOwnTranscribing.Value && isVadRecent) {
                    lastVoiceActiveAt = cpuClock.Now;
                    Log.LogInformation(
                        "IdleMonitor[{ChatId}]: bumped lastVoiceActiveAt to {LastVoiceActiveAt}",
                        activeChatId, lastVoiceActiveAt);
                }
            }

            var cActiveUntil = Hub.UserActivityUI.ActiveUntil.Computed;
            var userActiveUntil = cActiveUntil.Value;
            var effectiveActiveUntil = Moment.Max(userActiveUntil, lastVoiceActiveAt);

            var sessionFiresAt = lastConfirmedAt + Constants.Video.SessionConfirmInterval;
            var inactivityFiresAt = hasOwnStream
                ? effectiveActiveUntil + Constants.Video.SessionInactivityTimeout
                : Moment.MaxValue;
            var firesAt = Moment.Min(inactivityFiresAt, sessionFiresAt);
            var wait = (firesAt - cpuClock.Now).Positive();

            Log.LogInformation(
                "IdleMonitor[{ChatId}]: vadActiveHere={Vad}, hasOwnStream={HasOwnStream}, userActiveUntil={UserActiveUntil}, lastVoiceActiveAt={LastVoiceActiveAt}, effectiveActiveUntil={Effective}, inactivityFiresAt={InactivityFiresAt}, sessionFiresAt={SessionFiresAt}, wait={WaitSec}s",
                activeChatId, vadActiveHere, hasOwnStream, userActiveUntil, lastVoiceActiveAt, effectiveActiveUntil,
                inactivityFiresAt, sessionFiresAt, wait.TotalSeconds);

            if (wait > IdleMonitorEpsilon) {
                using var delayCts = cancellationToken.CreateLinkedTokenSource();
                var whenActiveChatChanges = cActiveChat.WhenInvalidated(cancellationToken);
                var whenActivityChanges = cActiveUntil.WhenInvalidated(cancellationToken);
                var whenAudioChanges = cAudioRecorderState.WhenInvalidated(cancellationToken);
                var whenTranscriptionChanges = cIsOwnTranscribing?.WhenInvalidated(cancellationToken)
                    ?? TaskExt.NeverEnding(delayCts.Token);
                var whenTimeout = Task.Delay(wait, delayCts.Token);
                await Task.WhenAny(whenActiveChatChanges, whenActivityChanges, whenAudioChanges, whenTranscriptionChanges, whenTimeout).ConfigureAwait(false);
                delayCts.CancelAndDisposeSilently();
                cActiveChat = await cActiveChat.Update(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var reason = inactivityFiresAt <= sessionFiresAt
                ? IdleReason.Inactivity
                : IdleReason.LongSession;
            var confirmed = await ShowSessionConfirm(activeChatId, reason, cancellationToken).ConfigureAwait(false);
            if (confirmed) {
                Log.LogInformation("IdleMonitor: {Reason} confirmed — continuing session for {ChatId}",
                    reason, activeChatId);
                // Clicking "Yes" naturally refreshes UserActivityUI.ActiveUntil too.
                lastConfirmedAt = cpuClock.Now;
            }
            else if (reason == IdleReason.Inactivity) {
                Log.LogWarning("IdleMonitor: {Reason} not confirmed — stopping own streams for {ChatId}",
                    reason, activeChatId);
                // Stop only own outgoing streams; watching/playback continues.
                StopStreaming();
            }
            else {
                Log.LogWarning("IdleMonitor: {Reason} not confirmed — ending session for {ChatId}",
                    reason, activeChatId);
                await EndSession().ConfigureAwait(false);
            }
            cActiveChat = await cActiveChat.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ShowSessionConfirm(ChatId chatId, IdleReason reason, CancellationToken cancellationToken)
    {
        string title;
        string text;
        if (reason == IdleReason.Inactivity) {
            var isRecording = await IsOwnCameraRecording(chatId, cancellationToken).ConfigureAwait(false);
            var isScreenCasting = await IsOwnScreenCasting(chatId, cancellationToken).ConfigureAwait(false);
            var idleFor = FormatMinutes(Constants.Video.SessionInactivityTimeout);
            title = L.Video_IdleTitle;
            text = (isRecording, isScreenCasting) switch {
                (true, true) => L.Video_IdleBothText_Format(idleFor),
                (false, true) => L.Video_IdleScreencastText_Format(idleFor),
                _ => L.Video_IdleRecordingText_Format(idleFor),
            };
        }
        else {
            title = L.Video_ContinueSessionTitle;
            text = L.Video_ContinueSessionText_Format(FormatMinutes(Constants.Video.SessionConfirmInterval));
        }

        var confirmed = false;
        var model = new ConfirmModal.Model(false, text, () => confirmed = true) {
            Title = title,
            ConfirmButtonText = L.Video_YesContinue,
            CancelButtonText = L.Video_StopNow,
        };
        Log.LogInformation("IdleMonitor: showing {Reason} modal for {ChatId}", reason, chatId);
        // ModalUI.Show needs the Blazor dispatcher; this runs on a worker chain.
        var modalRef = await Dispatcher
            .InvokeAsync(() => Hub.ModalUI.Show(model, cancellationToken))
            .ConfigureAwait(false);

        using var timeoutCts = cancellationToken.CreateLinkedTokenSource();
        timeoutCts.CancelAfter(Constants.Video.SessionConfirmModalTimeout);
        try {
            await modalRef.WhenClosed.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            await Dispatcher.InvokeAsync(() => modalRef.Close(force: true)).ConfigureAwait(false);
        }
        return confirmed;
    }

    private async Task EndSession()
    {
        // Long-session "no" or timeout: stop everything — video send/recv + audio.
        StopStreaming();
        CloseVideoPanel();
        await ChatAudioUI.ClearListeningChats().ConfigureAwait(false);
        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
        ChatAudioUI.StopReplay();
    }

    private string FormatMinutes(TimeSpan timeout)
    {
        var n = Math.Max(1, (int)Math.Round(timeout.TotalMinutes));
        return L.Video_Minutes(n, n);
    }

    // Nested types

    private enum IdleReason { Inactivity, LongSession }
}
