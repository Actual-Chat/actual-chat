using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.Video;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Typed wrapper around the JS VideoRecorder object reference.
/// Created via <see cref="Create"/> and disposed when recording session ends.
/// </summary>
public sealed class VideoRecorder : IAsyncDisposable
{
    private readonly TaskCompletionSource _whenStartedTaskCompletionSource = TaskCompletionSourceExt.New();
    private readonly TaskCompletionSource _whenStoppedTaskCompletionSource = TaskCompletionSourceExt.New();
    private readonly CancellationTokenSource _maintenanceCts = new ();
    private readonly Task _maintenanceTask;
    private IJSObjectReference _jsRef = null!;
    private DotNetObjectReference<RecorderCallbacks> _blazorCallbacksRef = null!;
    private (ChatId, bool)? _startRequest;
    private string _deviceId = "";
    private bool _isBlurEnabled;
    // True after Warmup() resolved and OpenGate() hasn't fired yet.
    // StateSync inspects this when an intent fires for an already-warm
    // recorder so it routes through OpenGate instead of StartRecording.
    public bool IsWarmedUp { get; private set; }

    private AppUIHub Hub { get; }
    private Session Session => Hub.Session;
    private IAuthors Authors => Hub.Authors;
    private ILiveVideoStreams LiveVideoStreams => Hub.LiveVideoStreams;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private IJSRuntime JS => Hub.JS;
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public VideoSourceKind Kind { get; }
    public Task WhenStopped => _whenStoppedTaskCompletionSource.Task;

    public static async Task<VideoRecorder> Create(AppUIHub hub, VideoSourceKind kind = VideoSourceKind.Camera)
    {
        var videoRecorder = new VideoRecorder(hub, kind);
        await videoRecorder.Initialize().ConfigureAwait(false);
        return videoRecorder;
    }

    private VideoRecorder(AppUIHub hub, VideoSourceKind kind)
    {
        Hub = hub;
        Kind = kind;
        _maintenanceTask = RunMaintenance(_whenStartedTaskCompletionSource.Task, _maintenanceCts.Token);
    }

    private async Task Initialize()
    {
        var blazorCallbacks = new RecorderCallbacks(Hub, this, Kind);
        _blazorCallbacksRef = DotNetObjectReference.Create(blazorCallbacks);
        var jsMethod = $"{BlazorUIAppModule.ImportName}.VideoRecorder.create";
        _jsRef = await JS
            .InvokeAsync<IJSObjectReference>(jsMethod, CancellationToken.None, _blazorCallbacksRef, (int)Kind)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _maintenanceCts.CancelAndDisposeSilently();
        await _maintenanceTask.SilentAwait();
        await _jsRef.DisposeSilentlyAsync("dispose").ConfigureAwait(false);
        _jsRef = null!;
        _blazorCallbacksRef.Dispose();
        _blazorCallbacksRef = null!;
    }

    // Recording lifecycle

    public async Task StartRecording(ChatId chatId, int maxLayerCount, CancellationToken cancellationToken)
    {
        if (_startRequest.HasValue)
            throw StandardError.Constraint("Start request already set");
        _startRequest = (chatId, true);
        var codecs = await GetInitialAudienceCodecs(chatId).ConfigureAwait(false);
        // Always-on simulcast: JS startRecording builds up to a 3-tier ladder
        // (probe-gated to 2-tier on iOS HW-encoder budget exhaustion), clamped
        // by maxLayerCount (mobile = 2 to keep the top output at 640x360 / L1).
        await _jsRef
            .InvokeVoidAsync("startRecording", cancellationToken, chatId.Value, codecs, maxLayerCount)
            .ConfigureAwait(false);
    }

    // Start the recorder pipeline in warmup mode: real camera frames flow
    // through encode, but the wire-gate is closed so nothing reaches the
    // server. OpenGate flips the gate open without restarting the encoder.
    // Used by JoinVideoCallModal so the encoder/HW slot is proven on real
    // frames during preview, then transitions seamlessly to live on Join.
    public async Task Warmup(ChatId chatId, CancellationToken cancellationToken)
    {
        if (_startRequest.HasValue)
            throw StandardError.Constraint("Start request already set");
        _startRequest = (chatId, true);
        var codecs = await GetInitialAudienceCodecs(chatId).ConfigureAwait(false);
        await _jsRef
            .InvokeVoidAsync("warmup", cancellationToken, chatId.Value, codecs)
            .ConfigureAwait(false);
        IsWarmedUp = true;
    }

    // Modal-to-live transition for a warm recorder. JS-side expands the
    // ladder to maxLayerCount tiers, flips the gate, forces a keyframe, and
    // fires OnRecordingStarted (which kicks RunMaintenance into its
    // subscription loop). No-op if the recorder isn't in warmup state.
    public async Task OpenGate(int maxLayerCount, CancellationToken cancellationToken)
    {
        if (!IsWarmedUp) {
            Log.LogWarning(nameof(OpenGate) + " called but recorder is not in warmup state");
            return;
        }
        await _jsRef
            .InvokeVoidAsync("openGate", cancellationToken, maxLayerCount)
            .ConfigureAwait(false);
        IsWarmedUp = false;
    }

    // Modal closed without Join. Tears down the warmup pipeline without
    // firing OnRecordingStarted (since the recording never officially
    // started). Safe to call on a non-warm recorder.
    public async Task CancelWarmup(CancellationToken cancellationToken)
    {
        if (!IsWarmedUp)
            return;
        await _jsRef
            .InvokeVoidAsync("cancelWarmup", cancellationToken)
            .ConfigureAwait(false);
        IsWarmedUp = false;
    }

    public async Task StartScreenCast(ChatId chatId, int maxLayerCount, CancellationToken cancellationToken)
    {
        if (_startRequest.HasValue)
            throw StandardError.Constraint("Start request already set");
        _startRequest = (chatId, false);
        var codecs = await GetInitialAudienceCodecs(chatId).ConfigureAwait(false);
        await _jsRef
            .InvokeVoidAsync("startScreenCast", cancellationToken, chatId.Value, codecs, maxLayerCount)
            .ConfigureAwait(false);
    }

    public Task StopRecording(CancellationToken cancellationToken)
        => _jsRef.InvokeVoidAsync("stopRecording", cancellationToken).AsTask();

    // Camera & blur

    public async Task SetSelectedCamera(string deviceId, CancellationToken cancellationToken)
    {
        _deviceId = deviceId;
        if (!string.IsNullOrEmpty(deviceId))
            await _jsRef.InvokeVoidAsync("setSelectedCamera", cancellationToken, deviceId).ConfigureAwait(false);
    }

    public Task SetBlurEnabled(bool enabled, CancellationToken cancellationToken)
    {
        _isBlurEnabled = enabled;
        return _jsRef.InvokeVoidAsync("setBlurEnabled", cancellationToken, enabled).AsTask();
    }

    public Task SwitchCamera(string deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(deviceId) || _deviceId == deviceId)
            return Task.CompletedTask;

        _deviceId = deviceId;
        return _jsRef.InvokeVoidAsync("switchCamera", cancellationToken, deviceId).AsTask();
    }

    public Task ToggleBlur(bool enabled, CancellationToken cancellationToken)
    {
        if (_isBlurEnabled == enabled)
            return Task.CompletedTask;

        _isBlurEnabled = enabled;
        return _jsRef.InvokeVoidAsync("toggleBlur", cancellationToken, enabled).AsTask();
    }

    // Pushes a layer ladder to the JS VideoRecorder. Hot-applied to a
    // running pipeline. Pass null or an empty list to collapse to single-encoder.
    public Task SetLayers(
        IReadOnlyList<VideoLayerDef>? layers,
        CancellationToken cancellationToken)
    {
        var arg = layers is { Count: > 0 }
            ? layers.Select(x => new {
                width = x.Width,
                height = x.Height,
                baseBitrateKbps = x.BaseBitrateKbps,
            }).ToArray()
            : null;
        return _jsRef.InvokeVoidAsync("setLayers", cancellationToken, (object?)arg).AsTask();
    }

    public Task SetTargetLayerCount(int layerCount, CancellationToken cancellationToken)
    {
        var ladder = BuildLadder(Kind);
        layerCount = Math.Min(layerCount, ladder.Count);
        var layers = layerCount <= 1
            ? null
            : ladder.Take(layerCount).ToArray();
        return SetLayers(layers, cancellationToken);
    }

    // Thermal fps ceiling; 0 = no ceiling.
    public Task SetFpsCeiling(int maxFps, CancellationToken cancellationToken)
        => _jsRef.InvokeVoidAsync("setFpsCeiling", cancellationToken, maxFps).AsTask();

    // Private methods

    private async Task RunMaintenance(Task startTrigger, CancellationToken cancellationToken)
    {
        await startTrigger.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startRequest = GetStartRequest();
        var chatId = startRequest.Item1;
        var t1 = SubscribeToKeyFrameRequests(chatId, cancellationToken);
        var t2 = SubscribeToSupportedDecoderCodecs(chatId, cancellationToken);
        var t3 = ForwardRemoteStreamCount(chatId, cancellationToken);
        var t4 = SubscribeToLayerDemand(chatId, cancellationToken);
        var t5 = SubscribeToVoiceActivity(chatId, cancellationToken);
        var t6 = SubscribeToThumbnailOnly(chatId, cancellationToken);
        await Task.WhenAll(t1, t2, t3, t4, t5, t6).ConfigureAwait(false);
    }

    private (ChatId, bool) GetStartRequest()
        => _startRequest ?? throw new InvalidOperationException("Start request not set");

    private void OnRecordingStarted()
        => _whenStartedTaskCompletionSource.TrySetResult();

    private void OnRecordingStopped()
    {
        _maintenanceCts.CancelSilently();
        _whenStoppedTaskCompletionSource.TrySetResult();
    }

    private void OnRecordingError()
    {
        _maintenanceCts.CancelSilently();
        _whenStoppedTaskCompletionSource.TrySetResult();
    }

    private Task OnRecorderStats(RecorderStats stats)
    {
        var isDotNetConnected = !Hub.ConnectivityUI.IsConnected.IsValue(out var v) || v;
        var effectiveStats = stats with {
            IsConnected = isDotNetConnected && stats.IsPeerConnected,
        };
        return Hub.VideoQualityUI.OnRecorderStats(Kind, effectiveStats, this, CancellationToken.None);
    }

    private async Task SubscribeToKeyFrameRequests(ChatId chatId, CancellationToken cancellationToken) {
        try {
            Log.LogInformation("SubscribeToKeyFrameRequests: starting for ChatId={ChatId}", chatId);

            // Wait for our own stream to appear (registration is async)
            StreamId? ownStreamId = null;
            var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (ownAuthor == null)
                return;

            for (var i = 0; i < 30; i++) { // poll up to 15s
                var streams = await ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
                var ownStream = streams.FirstOrDefault(s => s.AuthorId == ownAuthor.Id && s.SourceKind == Kind);
                if (ownStream != default) {
                    ownStreamId = ownStream.StreamId;
                    break;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            if (ownStreamId == null) {
                Log.LogWarning("SubscribeToKeyFrameRequests: own stream not found after polling for ChatId={ChatId}", chatId);
                return;
            }

            Log.LogInformation("SubscribeToKeyFrameRequests: found own stream #{StreamId}, subscribing", ownStreamId);

            var cState = await Computed.Capture(
                () => LiveVideoStreams.LastKeyframeRequestAt(Session, ownStreamId, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            var lastRequestAt = cState.Value;
            await foreach (var (requestAt, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                if (requestAt == lastRequestAt)
                    continue;

                lastRequestAt = requestAt;
                Log.LogInformation(
                    "Keyframe request: invoking forceKeyFrame interop for stream {StreamId}, requestAt={RequestAt}",
                    ownStreamId, requestAt);
                await _jsRef.InvokeVoidAsync("forceKeyFrame", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToKeyFrameRequests failed");
        }
    }

    private async Task SubscribeToLayerDemand(ChatId chatId, CancellationToken cancellationToken) {
        try {
            Log.LogInformation("SubscribeToLayerDemand: starting for ChatId={ChatId}", chatId);

            StreamId? ownStreamId = null;
            var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (ownAuthor == null)
                return;

            for (var i = 0; i < 30; i++) {
                var streams = await ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
                var ownStream = streams.FirstOrDefault(s => s.AuthorId == ownAuthor.Id && s.SourceKind == Kind);
                if (ownStream != default) {
                    ownStreamId = ownStream.StreamId;
                    break;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            if (ownStreamId == null) {
                Log.LogWarning("SubscribeToLayerDemand: own stream not found after polling for ChatId={ChatId}", chatId);
                return;
            }

            Log.LogInformation("SubscribeToLayerDemand: found own stream #{StreamId}, subscribing", ownStreamId);
            var lastMask = int.MinValue;
            var primaryFailures = 0;
            var primaryEverWorked = false;
            while (true) {
                try {
                    var cState = await Computed.Capture(
                        () => LiveVideoStreams.RequestedLayersMask(Session, ownStreamId, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    await foreach (var (mask, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                        primaryEverWorked = true;
                        primaryFailures = 0;
                        if (mask == lastMask)
                            continue;

                        lastMask = mask;
                        Log.LogInformation(
                            "RequestedLayersMask: stream {StreamId} → {Mask:b}", ownStreamId, mask);
                        await _jsRef.InvokeVoidAsync("setDemandedLayers", cancellationToken, mask).ConfigureAwait(false);
                    }
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                }
                catch (Exception e) {
                    // Only a server that never once answered is treated as "old" and
                    // downgraded to the legacy aggregate; a fault after the method has
                    // worked is transient — retry, don't lose demand-set for the session.
                    primaryFailures++;
                    if (!primaryEverWorked && primaryFailures >= 3) {
                        Log.LogWarning(e,
                            "SubscribeToLayerDemand: RequestedLayersMask unavailable, "
                            + "falling back to MaxRequestedLayerId");
                        break;
                    }
                    Log.LogWarning(e,
                        "SubscribeToLayerDemand: RequestedLayersMask faulted (attempt {Attempt}), retrying",
                        primaryFailures);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
            }
            var cMax = await Computed.Capture(
                () => LiveVideoStreams.MaxRequestedLayerId(Session, ownStreamId, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            var lastMax = int.MinValue;
            await foreach (var (maxLayerId, _) in cMax.Changes(cancellationToken).ConfigureAwait(false)) {
                if (maxLayerId == lastMax)
                    continue;

                lastMax = maxLayerId;
                var mask = maxLayerId < 0 ? 0 : (1 << (maxLayerId + 1)) - 1;
                Log.LogInformation(
                    "MaxRequestedLayerId (fallback): stream {StreamId} → {MaxLayerId}", ownStreamId, maxLayerId);
                await _jsRef.InvokeVoidAsync("setDemandedLayers", cancellationToken, mask).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToLayerDemand failed");
        }
    }

    // Forwards the "every active viewer displays me as a thumbnail" aggregate
    // to JS — the fps-shed input. Camera only: screencast never sheds.
    private async Task SubscribeToThumbnailOnly(ChatId chatId, CancellationToken cancellationToken) {
        if (Kind != VideoSourceKind.Camera)
            return;
        try {
            StreamId? ownStreamId = null;
            var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (ownAuthor == null)
                return;

            for (var i = 0; i < 30; i++) {
                var streams = await ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
                var ownStream = streams.FirstOrDefault(s => s.AuthorId == ownAuthor.Id && s.SourceKind == Kind);
                if (ownStream != default) {
                    ownStreamId = ownStream.StreamId;
                    break;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            if (ownStreamId == null) {
                Log.LogWarning("SubscribeToThumbnailOnly: own stream not found after polling for ChatId={ChatId}", chatId);
                return;
            }

            bool? lastValue = null;
            var failures = 0;
            var everWorked = false;
            while (true) {
                try {
                    var cState = await Computed.Capture(
                        () => LiveVideoStreams.ThumbnailViewersOnly(Session, ownStreamId, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    var changes = cState.Changes(cancellationToken);
                    await foreach (var (thumbnailOnly, _) in changes.ConfigureAwait(false)) {
                        everWorked = true;
                        failures = 0;
                        if (thumbnailOnly == lastValue)
                            continue;

                        lastValue = thumbnailOnly;
                        Log.LogInformation(
                            "ThumbnailViewersOnly: stream {StreamId} → {Value}", ownStreamId, thumbnailOnly);
                        await _jsRef.InvokeVoidAsync("setThumbnailOnly", cancellationToken, thumbnailOnly)
                            .ConfigureAwait(false);
                    }
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                }
                catch (Exception e) {
                    // Retry transient faults; only a never-answered server disables the
                    // shed (old server → no ThumbnailViewersOnly).
                    failures++;
                    if (!everWorked && failures >= 3) {
                        Log.LogWarning(e, "SubscribeToThumbnailOnly: unavailable, fps shed disabled");
                        await _jsRef.InvokeVoidAsync("setThumbnailOnly", cancellationToken, false)
                            .ConfigureAwait(false);
                        return;
                    }
                    Log.LogWarning(e,
                        "SubscribeToThumbnailOnly: faulted (attempt {Attempt}), retrying", failures);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToThumbnailOnly failed");
        }
    }

    // Forwards local voice-activity edges to JS so the sender keeps full fps
    // while the user is speaking, even when no peer is focusing them — pacing
    // down the active speaker would make them choppy the moment they talk.
    private async Task SubscribeToVoiceActivity(ChatId chatId, CancellationToken cancellationToken) {
        try {
            bool? lastSpeaking = null;
            await foreach (var (state, _) in Hub.AudioRecorder.State.Computed.Changes(cancellationToken).ConfigureAwait(false)) {
                var speaking = state.IsVoiceActive && state.ChatId == chatId;
                if (speaking == lastSpeaking)
                    continue;
                lastSpeaking = speaking;
                await _jsRef.InvokeVoidAsync("setSpeaking", cancellationToken, speaking).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToVoiceActivity failed");
        }
    }

    private async Task SubscribeToSupportedDecoderCodecs(ChatId chatId, CancellationToken cancellationToken) {
        try {
            Log.LogInformation("SubscribeToSupportedDecoderCodecs: starting for ChatId={ChatId}", chatId);
            var cState = await Computed.Capture(
                () => LiveVideoStreams.GetSupportedCodecs(Session, chatId, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            await foreach (var (codecs, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                Log.LogInformation("SubscribeToSupportedDecoderCodecs: received codecs=[{Codecs}]", string.Join(", ", codecs));
                await _jsRef.InvokeVoidAsync("updateSupportedDecoderCodecs", cancellationToken, (object)codecs.ToArray()).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToSupportedDecoderCodecs failed");
        }
    }

    // Forwards remote-stream count to JS for VAD-driven top-extra drop logic
    // (drops top simulcast extra during silence in group calls). Independent of
    // simulcast activation — that's now always-on at recording start.
    private async Task ForwardRemoteStreamCount(ChatId chatId, CancellationToken cancellationToken)
    {
        try {
            var lastCount = -1;
            var cState = await Computed.Capture(
                () => ChatVideoUI.GetRemoteStreams(chatId, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            await foreach (var (streamInfos, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                var count = streamInfos.Length;
                if (count == lastCount)
                    continue;

                lastCount = count;
                await _jsRef.InvokeVoidAsync("setRemoteStreamCount", cancellationToken, count)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "ForwardRemoteStreamCount failed");
        }
    }

    // Mode-aware ladder, sorted lowest → highest so index matches the layer-id
    // convention (0 = base, N = top). Each tier is ¼ pixels of the next.
    // Camera: 3-tier 720p/360p/180p. ScreenCast: 2-tier 1080p/540p.
    public static IReadOnlyList<VideoLayerDef> BuildLadder(VideoSourceKind kind)
        => kind == VideoSourceKind.Camera
            ? VideoLayerDef.CameraLayers
            : VideoLayerDef.ScreenCastLayers;

    private async Task<string[]> GetInitialAudienceCodecs(ChatId chatId) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var codecs = await LiveVideoStreams
                .GetSupportedCodecs(Session, chatId, cts.Token)
                .ConfigureAwait(false);
            Log.LogInformation("GetInitialAudienceCodecs: codecs=[{Codecs}]", string.Join(", ", codecs));
            return codecs.ToArray();
        }
        catch (OperationCanceledException) {
            Log.LogInformation("GetInitialAudienceCodecs: timed out, no audience codecs available");
        }
        catch (Exception e) {
            Log.LogWarning(e, "GetInitialAudienceCodecs failed");
        }
        return [];
    }

    // Nested types

    private sealed class RecorderCallbacks(AppUIHub hub, VideoRecorder videoRecorder, VideoSourceKind kind)
    {
        [JSInvokable]
        public void OnRecordingStarted()
        {
            videoRecorder.OnRecordingStarted();
            var startRequest = videoRecorder.GetStartRequest();
            var chatId = startRequest.Item1;
            hub.ChatVideoUI.OnRecordingStarted(chatId, kind);
        }

        [JSInvokable]
        public void OnRecordingStopped() {
            videoRecorder.OnRecordingStopped();
            hub.ChatVideoUI.OnRecordingStopped(kind);
        }

        [JSInvokable]
        public void OnRecordingError(string error)
        {
            videoRecorder.OnRecordingError();
            hub.ChatVideoUI.OnRecordingError(error, kind);
        }

        [JSInvokable]
        public void OnTrackSettings(string? deviceId, string? facingMode)
        {
            // Fires from JS after a camera track is acquired (start or camera
            // switch). Lets CameraUI resolve per-camera display preferences
            // (mirror) from current device + facingMode. Not called for
            // screencast — its display is never mirrored.
            if (kind == VideoSourceKind.Camera)
                hub.CameraUI.OnTrackSettings(deviceId, facingMode);
        }

        [JSInvokable]
        public Task OnRecorderStats(
            double encodeDeficitEma,
            double senderFrameDropRatioEma,
            double lastAckAgeMs,
            bool isPeerConnected,
            byte[] dropStages,
            int[] dropCounts,
            int bundlesShipped,
            int bundlesEncoded,
            long bytesEncoded,
            double encodeQueueDepthEma,
            double wireQueueDepthEma,
            double floodGateSkipPerSec,
            int peerReconnectStreak,
            int encoderRestartStreakIn60s,
            bool isTabBackgrounded,
            long wireAckedBytes,
            double encodeTimeMsMean = -1,
            double downscaleTimeMsMean = -1,
            double downscaleTimeMsMax = -1,
            int keepAliveFramesInjected = 0,
            bool isHardwareAccelerated = false,
            double wireMinRttMs = -1)
        {
            var dropTrace = new Dictionary<FrameDropStage, int>(dropStages.Length);
            for (var i = 0; i < dropStages.Length && i < dropCounts.Length; i++)
                dropTrace[(FrameDropStage)dropStages[i]] = dropCounts[i];
            return videoRecorder.OnRecorderStats(new RecorderStats(
                EncodeDeficitEma: encodeDeficitEma,
                SenderFrameDropRatioEma: senderFrameDropRatioEma,
                LastAckAgeMs: lastAckAgeMs,
                IsConnected: false,
                IsPeerConnected: isPeerConnected,
                DropTrace: dropTrace,
                BundlesShipped: bundlesShipped,
                BundlesEncoded: bundlesEncoded,
                BytesEncoded: bytesEncoded,
                EncodeQueueDepthEma: encodeQueueDepthEma,
                WireQueueDepthEma: wireQueueDepthEma,
                FloodGateSkipPerSec: floodGateSkipPerSec,
                PeerReconnectStreak: peerReconnectStreak,
                EncoderRestartStreakIn60s: encoderRestartStreakIn60s,
                IsTabBackgrounded: isTabBackgrounded,
                WireAckedBytes: wireAckedBytes,
                EncodeTimeMsMean: encodeTimeMsMean,
                DownscaleTimeMsMean: downscaleTimeMsMean,
                DownscaleTimeMsMax: downscaleTimeMsMax,
                KeepAliveFramesInjected: keepAliveFramesInjected,
                IsHardwareAccelerated: isHardwareAccelerated,
                WireMinRttMs: wireMinRttMs));
        }
    }
}
