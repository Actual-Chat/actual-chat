using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
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
            AsyncChain.From(SyncRecordingLifecycle),
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

    [ComputeMethod]
    protected virtual async Task<RecordingIntent?> GetRecordingIntent(CancellationToken cancellationToken)
    {
        var chatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        var isScreencasting = await _isScreencasting.Use(cancellationToken).ConfigureAwait(false);
        if (isScreencasting)
            return new ScreencastIntent(chatId);

        var cameraDeviceId = await _selectedCameraDeviceId.Use(cancellationToken).ConfigureAwait(false);
        var blurEnabled = await _isBackgroundBlurEnabled.Use(cancellationToken).ConfigureAwait(false);
        return new CameraRecordingIntent(chatId, cameraDeviceId, blurEnabled);
    }

    // Recording lifecycle

    private async Task SyncRecordingLifecycle(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetRecordingIntent(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        VideoRecorder? recorder = null;
        (StreamKind, ChatId)? activeChannel = null;

        try {
            await foreach (var (intent, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                if (intent is null || intent.Channel != activeChannel) {
                    if (recorder is not null) {
                        await CompleteRecording(recorder, cancellationToken).ConfigureAwait(false);
                        recorder = null;
                        activeChannel = null;
                    }
                }
                if (intent is null)
                    continue;

                if (recorder is null) {
                    // Recording should start
                    try {
                        _errorMessage.Value = null; // Clear any previous error
                        recorder = await VideoRecorder.Create(Hub).ConfigureAwait(false);
                        // Ensure server clock is synced before recording (TIMING_ANCHOR accuracy)
                        var serverTimeSync = Hub.Services.GetService<ServerTimeSync>();
                        if (serverTimeSync != null)
                            await serverTimeSync.EnsureSynced(cancellationToken).ConfigureAwait(false);

                        if (intent is ScreencastIntent screencastIntent) {
                            await recorder.StartScreencast(screencastIntent.ChatId, cancellationToken).ConfigureAwait(false);
                        } else if (intent is CameraRecordingIntent cameraIntent) {
                            await recorder.SetSelectedCamera(cameraIntent.CameraDeviceId ?? "", cancellationToken).ConfigureAwait(false);
                            await recorder.SetBlurEnabled(cameraIntent.BlurEnabled, cancellationToken).ConfigureAwait(false);
                            await recorder.StartRecording(cameraIntent.ChatId, cancellationToken).ConfigureAwait(false);
                        }
                        else
                            throw new InvalidOperationException($"Unexpected recording intent: {intent}");

                        activeChannel = intent.Channel;
                    }
                    catch (Exception e) when (e is not OperationCanceledException) {
                        OnRecordingError("Failed to start recording");
                        Log.LogWarning(e, "SyncRecordingLifecycle: failed to start recording");
                    }
                }
                else if (intent is CameraRecordingIntent cameraIntent) {
                    // Camera recording should be updated
                    try {
                        await recorder.SwitchCamera(cameraIntent.CameraDeviceId ?? "", cancellationToken).ConfigureAwait(false);
                        await recorder.ToggleBlur(cameraIntent.BlurEnabled, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException) {
                        Log.LogWarning(e, "SyncRecordingLifecycle: failed to update camera streaming settings");
                    }
                }
            }
        }
        finally {
            // TODO(DF): to think how to properly handle cancellation
            if (recorder is not null)
                await CompleteRecording(recorder, CancellationToken.None).ConfigureAwait(false);
        }
        return;

        static async Task CompleteRecording(VideoRecorder recorder, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancellationToken1 = cts.Token;
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            if (!recorder.WhenStopped.IsCompleted)
                await recorder.StopRecording(cancellationToken1).WaitAsync(cancellationToken1).ConfigureAwait(false);
            await recorder.WhenStopped.WaitAsync(cancellationToken1).ConfigureAwait(false);
            await recorder.DisposeAsync().AsTask().WaitAsync(cancellationToken1).ConfigureAwait(false);
        }
    }

    // Active speaker focus

    private async Task SyncFocusedSpeaker(CancellationToken cancellationToken)
    {
        var prevChatId = (ChatId?)null;
        var cState = await Computed
            .Capture(() => GetActiveSpeakerState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await foreach (var (state, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (chatId, speakingWithVideo, remoteAuthorIds, screencastAuthorId) = state;

            if (chatId != prevChatId) {
                ClearFocus();
                prevChatId = chatId;
            }

            if (chatId is null)
                continue;

            // Screencast always takes focus (no debounce)
            if (screencastAuthorId is not null) {
                var oldFocus = _focusedSpeakerId.Value;
                if (oldFocus != screencastAuthorId) {
                    if (oldFocus != null)
                        _previousFocusedSpeakerId.Value = oldFocus;
                    _focusedSpeakerId.Value = screencastAuthorId;
                }
                _focusDebounceCts?.Cancel();
                _focusDebounceCts = null;
                _pendingFocusCandidate = null;
                continue;
            }

            UpdateActiveSpeakers(speakingWithVideo);

            // Validate focused author is still among remote streams; fallback to first
            var currentFocus = _focusedSpeakerId.Value;
            if (currentFocus != null && remoteAuthorIds.Length > 0 && !remoteAuthorIds.Contains(currentFocus))
                _focusedSpeakerId.Value = null;
            if (_focusedSpeakerId.Value is null && remoteAuthorIds.Length > 0)
                _focusedSpeakerId.Value = remoteAuthorIds[0];
        }
    }

    [ComputeMethod]
    protected virtual async Task<ActiveSpeakerState> GetActiveSpeakerState(CancellationToken cancellationToken)
    {
        var isVideoEnabled = await IsVideoStreamingEnabled(cancellationToken).ConfigureAwait(false);
        if (!isVideoEnabled)
            return ActiveSpeakerState.None;

        var chatId = await Hub.ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return ActiveSpeakerState.None;

        var audioStreamingAuthorIds = await Hub.LiveStreamUI
            .GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var videoStreams = await GetActiveVideoStreams(chatId, cancellationToken)
            .ConfigureAwait(false);

        // Filter out own author
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var remoteVideoAuthorIds = videoStreams
            .Select(s => s.AuthorId)
            .Where(a => ownAuthor?.Id != a)
            .ToHashSet();

        // Check for screencast among remote streams (not own)
        var screencastAuthorId = videoStreams
            .Where(s => s.StreamKind == StreamKind.Screencast && ownAuthor?.Id != s.AuthorId)
            .Select(s => (AuthorId?)s.AuthorId)
            .FirstOrDefault();

        var speakingWithVideo = audioStreamingAuthorIds
            .Where(a => remoteVideoAuthorIds.Contains(a))
            .ToArray();

        return new ActiveSpeakerState(chatId, speakingWithVideo, remoteVideoAuthorIds.ToArray(), screencastAuthorId);
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

    protected abstract record RecordingIntent(ChatId ChatId)
    {
        public abstract (StreamKind, ChatId) Channel { get; }
    }

    protected sealed record CameraRecordingIntent(ChatId ChatId, string? CameraDeviceId, bool BlurEnabled)
        : RecordingIntent(ChatId)
    {
        public override (StreamKind, ChatId) Channel => (StreamKind.Webcam, ChatId);
    }

    protected sealed record ScreencastIntent(ChatId ChatId) : RecordingIntent(ChatId)
    {
        public override (StreamKind, ChatId) Channel => (StreamKind.Screencast, ChatId);
    }

    protected sealed record ActiveSpeakerState(ChatId? ChatId, AuthorId[] SpeakingWithVideo, AuthorId[] RemoteVideoAuthorIds, AuthorId? ScreencastAuthorId = null)
    {
        public static readonly ActiveSpeakerState None = new(null, [], []);
    }
}
