using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
using ActualChat.Video;
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

    // Recording lifecycle

    private async Task SyncRecordingLifecycle(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetRecordingIntent(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        ChatId? activeChatId = null;
        string? prevCameraDeviceId = null;
        var prevBlurEnabled = false;
        CancellationTokenSource? qualityCts = null;
        CancellationTokenSource? codecCts = null;
        CancellationTokenSource? remoteCountCts = null;

        try {
            await foreach (var (intent, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                if (intent.ChatId is not null && activeChatId is null) {
                    // Recording should start
                    try {
                        await EnsureJsRecorder().ConfigureAwait(false);

                        // Ensure server clock is synced before recording (TIMING_ANCHOR accuracy)
                        var serverTimeSync = Hub.Services.GetService<ServerTimeSync>();
                        if (serverTimeSync != null)
                            await serverTimeSync.EnsureSynced(cancellationToken).ConfigureAwait(false);

                        if (intent.IsScreencasting) {
                            var codecs = await GetInitialAudienceCodecs(intent.ChatId).ConfigureAwait(false);
                            await _jsRecorder!.InvokeVoidAsync("startScreencast", cancellationToken,
                                intent.ChatId.Value, codecs).ConfigureAwait(false);
                        } else {
                            if (!string.IsNullOrEmpty(intent.CameraDeviceId))
                                await _jsRecorder!.InvokeVoidAsync("setSelectedCamera", cancellationToken,
                                    intent.CameraDeviceId).ConfigureAwait(false);
                            await _jsRecorder!.InvokeVoidAsync("setBlurEnabled", cancellationToken,
                                intent.BlurEnabled).ConfigureAwait(false);
                            var codecs = await GetInitialAudienceCodecs(intent.ChatId).ConfigureAwait(false);
                            await _jsRecorder!.InvokeVoidAsync("startRecording", cancellationToken,
                                intent.ChatId.Value, codecs).ConfigureAwait(false);
                        }

                        activeChatId = intent.ChatId;
                        prevCameraDeviceId = intent.CameraDeviceId;
                        prevBlurEnabled = intent.BlurEnabled;

                        // Start quality/codec/remote-count subscriptions
                        qualityCts = new CancellationTokenSource();
                        codecCts = new CancellationTokenSource();
                        remoteCountCts = new CancellationTokenSource();
                        var linkedQualityCt = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, qualityCts.Token).Token;
                        var linkedCodecCt = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, codecCts.Token).Token;
                        var linkedRemoteCountCt = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, remoteCountCts.Token).Token;
                        _ = SubscribeToQualityRequests(activeChatId, linkedQualityCt);
                        _ = SubscribeToSupportedDecoderCodecs(activeChatId, linkedCodecCt);
                        _ = SyncRemoteStreamCount(activeChatId, linkedRemoteCountCt);
                    }
                    catch (Exception e) when (e is not OperationCanceledException) {
                        Log.LogWarning(e, "SyncRecordingLifecycle: failed to start recording");
                    }
                }
                else if (intent.ChatId is null && activeChatId is not null) {
                    // Recording should stop
                    CancelSubscriptions(ref qualityCts, ref codecCts, ref remoteCountCts);
                    if (_jsRecorder is not null) {
                        try {
                            await _jsRecorder.InvokeVoidAsync("stopRecording", cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is not OperationCanceledException) {
                            Log.LogWarning(e, "SyncRecordingLifecycle: failed to stop recording");
                        }
                    }
                    activeChatId = null;
                }
                else if (intent.ChatId is not null && activeChatId is not null && _jsRecorder is not null) {
                    // Recording ongoing — check for camera/blur changes
                    if (intent.CameraDeviceId != prevCameraDeviceId
                        && !string.IsNullOrEmpty(intent.CameraDeviceId)) {
                        try {
                            await _jsRecorder.InvokeVoidAsync("switchCamera", cancellationToken,
                                intent.CameraDeviceId).ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is not OperationCanceledException) {
                            Log.LogWarning(e, "SyncRecordingLifecycle: failed to switch camera");
                        }
                        prevCameraDeviceId = intent.CameraDeviceId;
                    }
                    if (intent.BlurEnabled != prevBlurEnabled) {
                        try {
                            await _jsRecorder.InvokeVoidAsync("toggleBlur", cancellationToken,
                                intent.BlurEnabled).ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is not OperationCanceledException) {
                            Log.LogWarning(e, "SyncRecordingLifecycle: failed to toggle blur");
                        }
                        prevBlurEnabled = intent.BlurEnabled;
                    }
                }
            }
        }
        finally {
            CancelSubscriptions(ref qualityCts, ref codecCts, ref remoteCountCts);
        }
    }

    [ComputeMethod]
    protected virtual async Task<RecordingIntent> GetRecordingIntent(CancellationToken cancellationToken)
    {
        var chatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return RecordingIntent.None;
        var cameraDeviceId = await _selectedCameraDeviceId.Use(cancellationToken).ConfigureAwait(false);
        var blurEnabled = await _isBackgroundBlurEnabled.Use(cancellationToken).ConfigureAwait(false);
        var isScreencasting = await _isScreencasting.Use(cancellationToken).ConfigureAwait(false);
        return new RecordingIntent(chatId, cameraDeviceId, blurEnabled, isScreencasting);
    }

    private async Task SubscribeToQualityRequests(ChatId chatId, CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("SubscribeToQualityRequests: starting for ChatId={ChatId}", chatId);

            // Wait for our own stream to appear (registration is async)
            StreamId? ownStreamId = null;
            var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (ownAuthor == null)
                return;

            for (var i = 0; i < 30; i++) { // poll up to 15s
                var streams = await GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
                var ownStream = streams.FirstOrDefault(s => s.AuthorId == ownAuthor.Id);
                if (ownStream != default) {
                    ownStreamId = ownStream.StreamId;
                    break;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            if (ownStreamId == null) {
                Log.LogWarning("SubscribeToQualityRequests: own stream not found after polling for ChatId={ChatId}", chatId);
                return;
            }

            Log.LogInformation("SubscribeToQualityRequests: found own stream #{StreamId}, subscribing", ownStreamId);

            VideoQualityPreset? lastAppliedPreset = null;
            while (true) {
                var computed = await Computed.Capture(
                    () => LiveVideoStreams.GetQualityPreset(Session, ownStreamId, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                var preset = computed.Value;
                Log.LogInformation("SubscribeToQualityRequests: received preset {Level} ({Width}x{Height} @ {Bitrate}bps), keyframe={KeyFrame}",
                    preset.Level, preset.Width, preset.Height, preset.Bitrate, preset.KeyFrameRequested);
                if (_jsRecorder is { } jsRef) {
                    // Only reconfigure encoder when resolution/bitrate/level actually changed.
                    var qualityChanged = lastAppliedPreset == null
                        || lastAppliedPreset.Level != preset.Level
                        || lastAppliedPreset.Width != preset.Width
                        || lastAppliedPreset.Height != preset.Height
                        || lastAppliedPreset.Bitrate != preset.Bitrate;
                    if (qualityChanged) {
                        await jsRef.InvokeVoidAsync("reconfigure", cancellationToken,
                            preset.Level.ToString(), preset.Width, preset.Height, preset.Bitrate)
                            .ConfigureAwait(false);
                        lastAppliedPreset = preset;
                    }
                    if (preset.KeyFrameRequested)
                        await jsRef.InvokeVoidAsync("forceKeyFrame", cancellationToken).ConfigureAwait(false);
                }
                await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToQualityRequests failed");
        }
    }

    private async Task SubscribeToSupportedDecoderCodecs(ChatId chatId, CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("SubscribeToSupportedDecoderCodecs: starting for ChatId={ChatId}", chatId);

            while (true) {
                var computed = await Computed.Capture(
                    () => LiveVideoStreams.GetSupportedCodecs(Session, chatId, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                var codecs = computed.Value;
                Log.LogInformation("SubscribeToSupportedDecoderCodecs: received codecs=[{Codecs}]", string.Join(", ", codecs));
                if (_jsRecorder is { } jsRef)
                    await jsRef.InvokeVoidAsync("updateSupportedDecoderCodecs", cancellationToken,
                        (object)codecs.ToArray()).ConfigureAwait(false);
                await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToSupportedDecoderCodecs failed");
        }
    }

    private async Task SyncRemoteStreamCount(ChatId chatId, CancellationToken cancellationToken)
    {
        try {
            var lastCount = -1;
            while (true) {
                var computed = await Computed.Capture(
                    () => GetRemoteStreams(chatId, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                var count = computed.Value.Length;
                if (count != lastCount && _jsRecorder is { } jsRef) {
                    lastCount = count;
                    await jsRef.InvokeVoidAsync("setRemoteStreamCount", cancellationToken, count)
                        .ConfigureAwait(false);
                }
                await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SyncRemoteStreamCount failed");
        }
    }

    private static void CancelSubscriptions(
        ref CancellationTokenSource? qualityCts,
        ref CancellationTokenSource? codecCts,
        ref CancellationTokenSource? remoteCountCts)
    {
        qualityCts.CancelAndDisposeSilently();
        qualityCts = null;
        codecCts.CancelAndDisposeSilently();
        codecCts = null;
        remoteCountCts.CancelAndDisposeSilently();
        remoteCountCts = null;
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

    protected sealed record RecordingIntent(
        ChatId? ChatId, string? CameraDeviceId, bool BlurEnabled, bool IsScreencasting)
    {
        public static readonly RecordingIntent None = new(null, null, false, false);
    }

    protected sealed record ActiveSpeakerState(ChatId? ChatId, AuthorId[] SpeakingWithVideo, AuthorId[] RemoteVideoAuthorIds, AuthorId? ScreencastAuthorId = null)
    {
        public static readonly ActiveSpeakerState None = new(null, [], []);
    }
}
