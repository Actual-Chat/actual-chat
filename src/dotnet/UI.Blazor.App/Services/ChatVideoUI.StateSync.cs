using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    private static readonly TimeSpan FocusDebounceDelay = TimeSpan.FromSeconds(1.5);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        var baseChains = new[] {
            AsyncChain.From(SyncFocusedSpeaker),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        await (
            from chain in baseChains
            select chain
                .WithTransiencyResolver(TransiencyResolvers.PreferTransient)
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SyncFocusedSpeaker(CancellationToken cancellationToken)
    {
        var prevChatId = (ChatId?)null;
        var cState = await Computed
            .Capture(() => GetActiveSpeakerState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await foreach (var (state, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            if (state is null)
                continue;

            var (chatId, speakingWithVideo) = state;

            if (chatId != prevChatId) {
                ClearFocus();
                prevChatId = chatId;
            }

            if (chatId is null)
                continue;

            UpdateActiveSpeakers(speakingWithVideo);
        }
    }

    [ComputeMethod]
    protected virtual async Task<ActiveSpeakerState> GetActiveSpeakerState(CancellationToken cancellationToken)
    {
        var chatId = await Hub.ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return ActiveSpeakerState.None;

        var audioStreamingAuthorIds = await Hub.LiveStreamUI
            .GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var videoStreamingAuthorIds = await GetVideoStreamingAuthorIds(chatId, cancellationToken)
            .ConfigureAwait(false);

        // Filter out own author
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var remoteVideoAuthorIds = videoStreamingAuthorIds
            .Where(a => ownAuthor?.Id != a)
            .ToHashSet();

        var speakingWithVideo = audioStreamingAuthorIds
            .Where(a => remoteVideoAuthorIds.Contains(a))
            .ToArray();

        return new ActiveSpeakerState(chatId, speakingWithVideo);
    }

    // Private methods

    private void UpdateActiveSpeakers(AuthorId[] speakingWithVideo)
    {
        var current = _focusedSpeakerId.Value;
        if (current != null && speakingWithVideo.Any(a => a == current)) {
            // Current focus is still speaking — keep it, cancel any pending switch
            _focusDebounceCts?.Cancel();
            _focusDebounceCts = null;
            _pendingFocusCandidate = null;
            return;
        }

        // No candidates — keep last focus
        if (speakingWithVideo.Length == 0)
            return;

        var candidate = speakingWithVideo[0];

        // Already debouncing this candidate
        if (_pendingFocusCandidate == candidate)
            return;

        // New candidate — start debounce
        _pendingFocusCandidate = candidate;
        _focusDebounceCts?.Cancel();
        _focusDebounceCts = new CancellationTokenSource();
        _ = DebouncedFocusSwitch(candidate, _focusDebounceCts.Token);
    }

    private void ClearFocus()
    {
        _focusDebounceCts?.Cancel();
        _focusDebounceCts = null;
        _pendingFocusCandidate = null;
        _focusedSpeakerId.Value = null;
        _previousFocusedSpeakerId.Value = null;
    }

    private async Task DebouncedFocusSwitch(AuthorId newSpeaker, CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(FocusDebounceDelay, cancellationToken).ConfigureAwait(false);
            var oldFocus = _focusedSpeakerId.Value;
            if (oldFocus != null && oldFocus != newSpeaker)
                _previousFocusedSpeakerId.Value = oldFocus;
            _focusedSpeakerId.Value = newSpeaker;
            _pendingFocusCandidate = null;
        }
        catch (OperationCanceledException) { }
    }

    // Nested types

    protected sealed record ActiveSpeakerState(ChatId? ChatId, AuthorId[] SpeakingWithVideo)
    {
        public static readonly ActiveSpeakerState None = new(null, []);
    }
}
