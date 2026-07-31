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
    // Demand invalidation is edge-only; re-assert the current aggregate this
    // often so a push lost anywhere along the chain heals without an edge.
    private static readonly TimeSpan DemandReassertPeriod = TimeSpan.FromSeconds(30);

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
        var t4 = SubscribeToDemand(chatId, cancellationToken);
        var t5 = SubscribeToVoiceActivity(chatId, cancellationToken);
        await Task.WhenAll(t1, t2, t3, t4, t5).ConfigureAwait(false);
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
        await RunPerOwnStreamSubscription(
            chatId, nameof(SubscribeToKeyFrameRequests), SubscribeToKeyFrameRequestsCore, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SubscribeToKeyFrameRequestsCore(StreamId ownStreamId, CancellationToken cancellationToken) {
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

    private async Task SubscribeToDemand(ChatId chatId, CancellationToken cancellationToken) {
        await RunPerOwnStreamSubscription(
            chatId, nameof(SubscribeToDemand), SubscribeToDemandCore, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SubscribeToDemandCore(StreamId ownStreamId, CancellationToken cancellationToken) {
        try {
            var lastMask = int.MinValue;
            bool? lastThumbnailOnly = null;
            var failures = 0;
            var everWorked = false;
            while (true) {
                try {
                    var cState = await Computed.Capture(
                        () => LiveVideoStreams.DemandInfo(Session, ownStreamId, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    while (true) {
                        cState = await cState.Update(cancellationToken).ConfigureAwait(false);
                        var info = cState.Value;
                        everWorked = true;
                        failures = 0;
                        // Dedupe state advances only AFTER a successful JS push:
                        // a failed interop call must be retried, not swallowed.
                        if (info.Mask != lastMask) {
                            Log.LogInformation(
                                "DemandInfo: stream {StreamId} → mask {Mask:b}", ownStreamId, info.Mask);
                            await _jsRef.InvokeVoidAsync("setDemandedLayers", cancellationToken, info.Mask)
                                .ConfigureAwait(false);
                            lastMask = info.Mask;
                        }
                        // Camera only: screencast never sheds fps.
                        if (Kind == VideoSourceKind.Camera && info.ThumbnailViewersOnly != lastThumbnailOnly) {
                            Log.LogInformation(
                                "DemandInfo: stream {StreamId} → thumbnailOnly {Value}",
                                ownStreamId, info.ThumbnailViewersOnly);
                            await _jsRef
                                .InvokeVoidAsync("setThumbnailOnly", cancellationToken, info.ThumbnailViewersOnly)
                                .ConfigureAwait(false);
                            lastThumbnailOnly = info.ThumbnailViewersOnly;
                        }
                        try {
                            await cState.WhenInvalidated(cancellationToken)
                                .WaitAsync(DemandReassertPeriod, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (TimeoutException) {
                            // Invalidation is edge-only, so a push lost anywhere along
                            // the chain would otherwise stay wrong until the aggregate
                            // happens to change — re-assert the current value instead.
                            lastMask = int.MinValue;
                            lastThumbnailOnly = null;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                }
                catch (Exception e) {
                    // Only a server that never once answered is treated as "old" and
                    // downgraded to the legacy per-question methods; a fault after
                    // the method has worked is transient — retry.
                    failures++;
                    if (!everWorked && failures >= 3) {
                        Log.LogWarning(e,
                            "SubscribeToDemand: DemandInfo unavailable, falling back to legacy methods");
                        break;
                    }
                    Log.LogWarning(e,
                        "SubscribeToDemand: DemandInfo faulted (attempt {Attempt}), retrying", failures);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
            }

            await Task.WhenAll(
                    SubscribeToLayerDemandLegacy(ownStreamId, cancellationToken),
                    SubscribeToThumbnailOnlyLegacy(ownStreamId, cancellationToken))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToDemand failed");
        }
    }

#pragma warning disable CS0618 // old-server fallback intentionally calls the obsolete methods
    // Old-server (pre-DemandInfo) chain: RequestedLayersMask, then
    // MaxRequestedLayerId as the last resort.
    private async Task SubscribeToLayerDemandLegacy(StreamId ownStreamId, CancellationToken cancellationToken) {
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

                    Log.LogInformation(
                        "RequestedLayersMask (legacy): stream {StreamId} → {Mask:b}", ownStreamId, mask);
                    await _jsRef.InvokeVoidAsync("setDemandedLayers", cancellationToken, mask).ConfigureAwait(false);
                    lastMask = mask;
                }
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) {
                // Only a server that never once answered is downgraded to the legacy
                // aggregate; a fault after the method has worked is transient — retry,
                // don't lose demand-set for the session.
                primaryFailures++;
                if (!primaryEverWorked && primaryFailures >= 3) {
                    Log.LogWarning(e,
                        "SubscribeToLayerDemandLegacy: RequestedLayersMask unavailable, "
                        + "falling back to MaxRequestedLayerId");
                    break;
                }
                Log.LogWarning(e,
                    "SubscribeToLayerDemandLegacy: RequestedLayersMask faulted (attempt {Attempt}), retrying",
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

            var mask = maxLayerId < 0 ? 0 : (1 << (maxLayerId + 1)) - 1;
            Log.LogInformation(
                "MaxRequestedLayerId (fallback): stream {StreamId} → {MaxLayerId}", ownStreamId, maxLayerId);
            await _jsRef.InvokeVoidAsync("setDemandedLayers", cancellationToken, mask).ConfigureAwait(false);
            lastMax = maxLayerId;
        }
    }
#pragma warning restore CS0618

    private async Task RunPerOwnStreamSubscription(
        ChatId chatId,
        string name,
        Func<StreamId, CancellationToken, Task> subscribe,
        CancellationToken cancellationToken)
    {
        // A recording's StreamId is NOT stable: any wire-sender restart (codec
        // switch, reconnect, ladder rebuild) issues a fresh PushStream and the
        // server mints a new id — viewers' demand/PLI state follows it, so a
        // subscription pinned to the first id goes stale after the first restart.
        // Tracks the own stream id reactively; restarts `subscribe` on change.
        Log.LogInformation("{Name}: starting for ChatId={ChatId}", name, chatId);
        Task? worker = null;
        CancellationTokenSource? workerCts = null;
        try {
            var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (ownAuthor == null)
                return;

            var currentStreamId = (StreamId?)null;
            while (true) {
                try {
                    var cState = await Computed.Capture(
                        () => ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    await foreach (var (streams, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                        var ownStream = streams.FirstOrDefault(
                            s => s.AuthorId == ownAuthor.Id && s.SourceKind == Kind);
                        var newStreamId = ownStream == default ? (StreamId?)null : ownStream.StreamId;
                        if (newStreamId == currentStreamId)
                            continue;

                        Log.LogInformation(
                            "{Name}: own stream {OldStreamId} → {NewStreamId}", name, currentStreamId, newStreamId);
                        currentStreamId = newStreamId;
                        workerCts.CancelAndDisposeSilently();
                        if (worker != null)
                            await worker.SilentAwait(false);
                        worker = null;
                        workerCts = null;
                        if (newStreamId is { } streamId) {
                            workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            var workerToken = workerCts.Token;
                            worker = Task.Run(() => subscribe(streamId, workerToken), workerToken);
                        }
                    }
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    return;
                }
                catch (Exception e) {
                    Log.LogWarning(e, "{Name}: stream watcher faulted, retrying", name);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "{Name} failed", name);
        }
        finally {
            workerCts.CancelAndDisposeSilently();
            if (worker != null)
                await worker.SilentAwait(false);
        }
    }

#pragma warning disable CS0618 // old-server fallback intentionally calls the obsolete method
    private async Task SubscribeToThumbnailOnlyLegacy(StreamId ownStreamId, CancellationToken cancellationToken) {
        // Camera only: screencast never sheds fps.
        if (Kind != VideoSourceKind.Camera)
            return;

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

                    Log.LogInformation(
                        "ThumbnailViewersOnly (legacy): stream {StreamId} → {Value}", ownStreamId, thumbnailOnly);
                    await _jsRef.InvokeVoidAsync("setThumbnailOnly", cancellationToken, thumbnailOnly)
                        .ConfigureAwait(false);
                    lastValue = thumbnailOnly;
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
                    Log.LogWarning(e, "SubscribeToThumbnailOnlyLegacy: unavailable, fps shed disabled");
                    await _jsRef.InvokeVoidAsync("setThumbnailOnly", cancellationToken, false)
                        .ConfigureAwait(false);
                    return;
                }
                Log.LogWarning(e,
                    "SubscribeToThumbnailOnlyLegacy: faulted (attempt {Attempt}), retrying", failures);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
    }
#pragma warning restore CS0618

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
                // Dispose nulls _jsRef out while this loop is still iterating
                if (_jsRef is not { } jsRef)
                    return;

                await jsRef.InvokeVoidAsync("setRemoteStreamCount", cancellationToken, count)
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
            double wireMinRttMs = -1,
            double wireRingDepthEma = -1)
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
                WireMinRttMs: wireMinRttMs,
                WireRingDepthEma: wireRingDepthEma));
        }
    }
}
