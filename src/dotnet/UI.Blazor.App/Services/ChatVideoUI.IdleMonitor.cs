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

        while (!cancellationToken.IsCancellationRequested) {
            var activeChatId = cActiveChat.Value;
            if (activeChatId is null) {
                sessionChatId = null;
                lastVoiceActiveAt = default;
                cActiveChat = await cActiveChat
                    .When(x => x is not null, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (sessionChatId != activeChatId) {
                sessionChatId = activeChatId;
                lastConfirmedAt = cpuClock.Now;
                lastVoiceActiveAt = default;
                Log.LogInformation("IdleMonitor: session started for {ChatId}", activeChatId);
            }

            // Inactivity timer applies only when the user is the sender
            // (recording or screencasting). Pure watching is not stopped here.
            var ownSourceKind = await GetOwnSourceKind(activeChatId, cancellationToken).ConfigureAwait(false);
            var hasOwnStream = ownSourceKind is not null;

            // Voice extension: while VAD reports speech in the same chat,
            // anchor inactivity to the latest voice-active moment so a user
            // talking into the camera doesn't trip the 15-min DOM-idle deadline.
            var cAudioRecorderState = Hub.AudioRecorder.State.Computed;
            var audioRecorderState = cAudioRecorderState.Value;
            var isVoiceActiveHere = hasOwnStream
                && audioRecorderState.IsVoiceActive
                && audioRecorderState.ChatId == activeChatId;
            if (isVoiceActiveHere)
                lastVoiceActiveAt = cpuClock.Now;

            var cActiveUntil = Hub.UserActivityUI.ActiveUntil.Computed;
            var userActiveUntil = cActiveUntil.Value;
            var effectiveActiveUntil = Moment.Max(userActiveUntil, lastVoiceActiveAt);

            var sessionFiresAt = lastConfirmedAt + Constants.Video.SessionConfirmInterval;
            var inactivityFiresAt = hasOwnStream
                ? effectiveActiveUntil + Constants.Video.SessionInactivityTimeout
                : Moment.MaxValue;
            var firesAt = Moment.Min(inactivityFiresAt, sessionFiresAt);
            var wait = (firesAt - cpuClock.Now).Positive();

            if (wait > IdleMonitorEpsilon) {
                using var delayCts = cancellationToken.CreateLinkedTokenSource();
                var whenActiveChatChanges = cActiveChat.WhenInvalidated(cancellationToken);
                var whenActivityChanges = cActiveUntil.WhenInvalidated(cancellationToken);
                var whenAudioChanges = cAudioRecorderState.WhenInvalidated(cancellationToken);
                var whenTimeout = Task.Delay(wait, delayCts.Token);
                await Task.WhenAny(whenActiveChatChanges, whenActivityChanges, whenAudioChanges, whenTimeout).ConfigureAwait(false);
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
            var what = (isRecording, isScreenCasting) switch {
                (true, true) => "video recording and screencasting",
                (false, true) => "screencasting",
                _ => "video recording",
            };
            title = "Still there?";
            text = $"You've been inactive for {FormatMinutes(Constants.Video.SessionInactivityTimeout)}."
                + $" Continue {what}?";
        }
        else {
            title = "Continue video session?";
            text = $"You've been on this video session for {FormatMinutes(Constants.Video.SessionConfirmInterval)}."
                + " Continue?";
        }

        var confirmed = false;
        var model = new ConfirmModal.Model(false, text, () => confirmed = true) {
            Title = title,
            ConfirmButtonText = "Yes, continue",
            CancelButtonText = "Stop now",
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

    private static string FormatMinutes(TimeSpan timeout)
    {
        var n = Math.Max(1, (int)Math.Round(timeout.TotalMinutes));
        return $"{n} {"minute".Pluralize(n)}";
    }

    // Nested types

    private enum IdleReason { Inactivity, LongSession }
}
