using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.Video;

namespace ActualChat.UI.Blazor.App.Services;

// One simulcast layer — matches the JS SpatialLayerConfig structure. PascalCase
// property names serialize to JS-friendly camelCase via Blazor JS interop's default
// JsonNamingPolicy.CamelCase.
public sealed record SpatialLayerSpec(int Width, int Height, int Bitrate);

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

    // Combined remote-presence state for the simulcast decision.
    // `_incomingStreamCount` = number of remote streams I'm subscribed to (from
    // ChatVideoUI.GetRemoteStreams). `_viewerCount` = number of peers subscribed
    // to MY stream (from VideoQualityPreset.ViewerCount, server-driven).
    // Either signal can independently arm simulcast — covers the asymmetric
    // publisher case where peer P pushes, peer V watches but never pushes back.
    // Negative = unknown / not yet observed. Both feed into ApplySimulcastDecision.
    private int _incomingStreamCount = -1;
    private int _viewerCount = -1;
    private bool _lastSimulcastActive;
    // Last server-aggregated spatial cap applied to the JS ladder. int.MaxValue =
    // no cap (full ladder). Tracked so we only push a new ladder when the cap
    // actually changes — avoids re-sending on every QualityPreset tick.
    private int _lastMaxSpatial = int.MaxValue;
    private readonly SemaphoreSlim _simulcastDecisionLock = new(1, 1);

    private AppUIHub Hub { get; }
    private StreamKind Kind { get; }
    private Session Session => Hub.Session;
    private IJSRuntime JS => Hub.JS;
    private IAuthors Authors => Hub.Authors;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private ILiveVideoStreams LiveVideoStreams => ChatVideoUI.LiveVideoStreams;
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public Task WhenStopped => _whenStoppedTaskCompletionSource.Task;

    public static async Task<VideoRecorder> Create(AppUIHub hub, StreamKind kind = StreamKind.Webcam)
    {
        var videoRecorder = new VideoRecorder(hub, kind);
        await videoRecorder.Initialize().ConfigureAwait(false);
        return videoRecorder;
    }

    private VideoRecorder(AppUIHub hub, StreamKind kind)
    {
        Hub = hub;
        Kind = kind;
        _maintenanceTask = RunMaintenance(_whenStartedTaskCompletionSource.Task, _maintenanceCts.Token);
    }

    private async Task Initialize()
    {
        var blazorCallbacks = new RecorderCallbacks(ChatVideoUI, this, Kind);
        _blazorCallbacksRef = DotNetObjectReference.Create(blazorCallbacks);
        var jsMethod = $"{BlazorUIAppModule.ImportName}.VideoRecorder.create";
        _jsRef = await JS
            .InvokeAsync<IJSObjectReference>(jsMethod, CancellationToken.None, _blazorCallbacksRef, (int)Kind)
            .ConfigureAwait(false);
    }

    // Recording lifecycle

    public async Task StartRecording(ChatId chatId, CancellationToken cancellationToken)
    {
        if (_startRequest.HasValue)
            throw StandardError.Constraint("Start request already set");
        _startRequest = (chatId, true);
        var codecs = await GetInitialAudienceCodecs(chatId).ConfigureAwait(false);
        // Seed simulcast layers BEFORE JS startRecording builds its config —
        // otherwise the initial pipeline spins up in P2P mode and a later
        // SetSimulcastLayers must pay a stop/restart cycle to apply.
        await SeedSimulcastLayers(chatId, cancellationToken).ConfigureAwait(false);
        await _jsRef.InvokeVoidAsync("startRecording", cancellationToken, chatId.Value, codecs).ConfigureAwait(false);
    }

    public async Task StartScreencast(ChatId chatId, CancellationToken cancellationToken)
    {
        if (_startRequest.HasValue)
            throw StandardError.Constraint("Start request already set");
        _startRequest = (chatId, false);
        var codecs = await GetInitialAudienceCodecs(chatId).ConfigureAwait(false);
        await _jsRef.InvokeVoidAsync("startScreencast", cancellationToken, chatId.Value, codecs).ConfigureAwait(false);
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

    // Pushes a simulcast layer ladder to the JS VideoRecorder. Hot-applied to a
    // running pipeline via Option C. Pass null or an empty list to disable
    // simulcast. `force = true` bypasses the JS-side downgrade-refusal guard
    // (`hasHigherTopTier`) — required for server-driven cap shrinks where the
    // intent is explicitly to reduce layers (cap dropped because all viewers
    // want low/mid). The default `force = false` preserves the guard so a
    // C# default ladder push doesn't downgrade a JS probe-promoted ladder.
    public Task SetSimulcastLayers(
        IReadOnlyList<SpatialLayerSpec>? layers,
        CancellationToken cancellationToken,
        bool force = false)
    {
        var arg = layers is { Count: > 0 } ? layers.ToArray() : null;
        return _jsRef.InvokeVoidAsync("setSimulcastLayers", cancellationToken, (object?)arg, force).AsTask();
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

    private async Task RunMaintenance(Task startTrigger, CancellationToken cancellationToken)
    {
        await startTrigger.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startRequest = GetStartRequest();
        var chatId = startRequest.Item1;
        var t1 = SubscribeToQualityRequests(chatId, cancellationToken);
        var t2 = SubscribeToSupportedDecoderCodecs(chatId, cancellationToken);
        var t3 = SyncRemoteStreamCount(chatId, cancellationToken);
        await Task.WhenAll(t1, t2, t3).ConfigureAwait(false);
    }

    private async Task SubscribeToQualityRequests(ChatId chatId, CancellationToken cancellationToken) {
        try {
            Log.LogInformation("SubscribeToQualityRequests: starting for ChatId={ChatId}", chatId);

            // Wait for our own stream to appear (registration is async)
            StreamId? ownStreamId = null;
            var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (ownAuthor == null)
                return;

            for (var i = 0; i < 30; i++) { // poll up to 15s
                var streams = await ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
                var ownStream = streams.FirstOrDefault(s => s.AuthorId == ownAuthor.Id && s.StreamKind == Kind);
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
            var cState = await Computed.Capture(
                () => LiveVideoStreams.GetQualityPreset(Session, ownStreamId, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            await foreach (var (preset, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                Log.LogInformation("SubscribeToQualityRequests: received preset {Level} ({Width}x{Height}), keyframe={KeyFrame}, viewers={ViewerCount}",
                    preset.Level, preset.Width, preset.Height, preset.IsKeyFrameRequested, preset.ViewerCount);
                // Only reconfigure when the server actually changes the preset.
                // On the first iteration we know nothing about network/CPU yet, so the
                // default "High" preset is noise — seed lastAppliedPreset silently.
                // Applies to both webcam and screencast: every stream adapts to
                // server-driven quality decisions; the startup noise is filtered by
                // the "first iteration" seed, not by StreamKind.
                if (lastAppliedPreset == null)
                    lastAppliedPreset = preset;
                else {
                    var qualityChanged = lastAppliedPreset.Level != preset.Level
                        || lastAppliedPreset.Width != preset.Width
                        || lastAppliedPreset.Height != preset.Height;
                    if (qualityChanged) {
                        await _jsRef.InvokeVoidAsync("reconfigure", cancellationToken,
                            preset.Level.ToString(), preset.Width, preset.Height).ConfigureAwait(false);
                        lastAppliedPreset = preset;
                    }
                }
                if (preset.IsKeyFrameRequested)
                    await _jsRef.InvokeVoidAsync("forceKeyFrame", cancellationToken).ConfigureAwait(false);
                // Viewer count from the server arms simulcast even when we have
                // no incoming streams (asymmetric publisher case).
                if (preset.ViewerCount != _viewerCount) {
                    _viewerCount = preset.ViewerCount;
                    await ApplySimulcastDecision(cancellationToken).ConfigureAwait(false);
                }
                // G1: server-aggregated spatial cap. Server's EvaluateQuality
                // reports max-across-peers EffectiveMaxSpatial — the highest
                // layer any subscriber currently needs. When this drops below
                // our full ladder, clamp the JS ladder so the worker spins
                // down upper extras and saves encode cost. When it raises,
                // restore the full ladder. force=true bypasses the JS-side
                // promotion guard since a cap-driven shrink is explicit policy.
                if (preset.MaxSpatialLayer != _lastMaxSpatial) {
                    _lastMaxSpatial = preset.MaxSpatialLayer;
                    if (Kind == StreamKind.Webcam && _lastSimulcastActive) {
                        var clamped = BuildClampedLadder(preset.MaxSpatialLayer);
                        Log.LogInformation(
                            "SubscribeToQualityRequests: MaxSpatialLayer={Cap} → ladder {Layers} layer(s)",
                            preset.MaxSpatialLayer, clamped?.Count ?? 0);
                        await SetSimulcastLayers(clamped, cancellationToken, force: true).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToQualityRequests failed");
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

    private async Task SyncRemoteStreamCount(ChatId chatId, CancellationToken cancellationToken)
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

                _incomingStreamCount = count;
                await ApplySimulcastDecision(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SyncRemoteStreamCount failed");
        }
    }

    // Single decision point for simulcast activation. Combines the local
    // incoming-stream count with the server-reported viewer count so either
    // signal can arm simulcast. Webcam only — screencast keeps the single-
    // encoder full-res path for text legibility. Idempotent on repeated
    // calls with the same effective count.
    private async Task ApplySimulcastDecision(CancellationToken cancellationToken)
    {
        if (Kind != StreamKind.Webcam)
            return;
        await _simulcastDecisionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            // Treat unknown signals as 0 for the threshold check so a single
            // available signal can arm/disarm. Threshold = MinMembersForSimulcast - 1
            // because "remote count" excludes the publisher itself.
            var localCount = Math.Max(0, _incomingStreamCount);
            var viewerCount = Math.Max(0, _viewerCount);
            var effectiveCount = Math.Max(localCount, viewerCount);
            var threshold = Constants.Video.MinMembersForSimulcast - 1;
            var simulcastActive = effectiveCount >= threshold;
            if (simulcastActive == _lastSimulcastActive)
                return;
            _lastSimulcastActive = simulcastActive;
            var ladder = simulcastActive ? BuildClampedLadder(_lastMaxSpatial) : null;
            Log.LogInformation(
                "ApplySimulcastDecision: incoming={Incoming}, viewers={Viewers}, effective={Effective}, simulcast={Simulcast}, cap={Cap}, layers={Layers}",
                _incomingStreamCount, _viewerCount, effectiveCount, simulcastActive, _lastMaxSpatial, ladder?.Count ?? 0);
            await SetSimulcastLayers(ladder, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _simulcastDecisionLock.Release();
        }
    }

    // 4-tier simulcast ladder, sorted lowest → highest so index matches the
    // spatial-id convention used everywhere (0 = base, N = top). Top entry is
    // 1080p, matching the JS-side single-encoder probe-promotion ceiling — so a
    // pipeline that started solo at 1080p can keep that resolution as the wire-
    // top once simulcast activates (without it, hot-adding extras only up to
    // 720p left spatial=0=1080p as a stranded "bonus" tier the receiver-side
    // filter can't reach via the spatial-id-ordered cap). Dims mirror
    // VideoQualityLevel steps; bitrates tuned for H.264 baseline headroom.
    private static IReadOnlyList<SpatialLayerSpec> BuildSimulcastLadder()
        => new SpatialLayerSpec[] {
            new(320, 180, 300_000),
            new(640, 360, 700_000),
            new(1280, 720, 2_000_000),
            new(1920, 1080, 2_300_000),
        };

    // Clamps the full simulcast ladder by the per-peer aggregate spatial cap.
    // `maxSpatial == int.MaxValue` (or any value >= ladder.Count - 1) returns
    // the unclamped ladder. `maxSpatial == 0` returns the base layer only,
    // forcing the worker into single-encoder mode (no extras). Negative caps
    // collapse to base for safety. Returns null when the result would have
    // fewer than 2 tiers — that's the JS-side "simulcast off" signal.
    private static IReadOnlyList<SpatialLayerSpec>? BuildClampedLadder(int maxSpatial)
    {
        var full = BuildSimulcastLadder();
        var keep = maxSpatial >= full.Count ? full.Count : Math.Max(1, maxSpatial + 1);
        if (keep < 2)
            return null;
        if (keep == full.Count)
            return full;
        return full.Take(keep).ToArray();
    }

    // Queries current remote stream count and pushes a ladder to JS before the
    // pipeline spins up. Screencast stays single-encoder. Best-effort — any
    // failure is logged and the session falls back to P2P mode.
    private async Task SeedSimulcastLayers(ChatId chatId, CancellationToken cancellationToken)
    {
        if (Kind != StreamKind.Webcam) {
            Log.LogInformation("SeedSimulcastLayers: skipped (kind={Kind})", Kind);
            return;
        }
        try {
            var streams = await ChatVideoUI.GetRemoteStreams(chatId, cancellationToken).ConfigureAwait(false);
            var count = streams.Length;
            var threshold = Constants.Video.MinMembersForSimulcast - 1;
            if (count < threshold) {
                Log.LogInformation(
                    "SeedSimulcastLayers: remote count={Count} < {Threshold} — staying in P2P",
                    count, threshold);
                return;
            }
            // Seed honors the latest known cap if QualityPreset already pushed one
            // (rare on a fresh start, but possible if the publisher restarted).
            var ladder = BuildClampedLadder(_lastMaxSpatial);
            Log.LogInformation(
                "SeedSimulcastLayers: remote count={Count} >= {Threshold} — activating {Layers}-layer simulcast (cap={Cap})",
                count, threshold, ladder?.Count ?? 0, _lastMaxSpatial);
            await SetSimulcastLayers(ladder, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SeedSimulcastLayers failed");
        }
    }

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

    private sealed class RecorderCallbacks(ChatVideoUI owner, VideoRecorder videoRecorder, StreamKind kind)
    {
        [JSInvokable]
        public void OnRecordingStarted()
        {
            videoRecorder.OnRecordingStarted();
            var startRequest = videoRecorder.GetStartRequest();
            var chatId = startRequest.Item1;
            owner.OnRecordingStarted(chatId, kind);
        }

        [JSInvokable]
        public void OnRecordingStopped() {
            videoRecorder.OnRecordingStopped();
            owner.OnRecordingStopped(kind);
        }

        [JSInvokable]
        public void OnRecordingError(string error)
        {
            videoRecorder.OnRecordingError();
            owner.OnRecordingError(error, kind);
        }

        [JSInvokable]
        public void OnTrackSettings(string? deviceId, string? facingMode)
        {
            // Fires from JS after a webcam track is acquired (start or camera
            // switch). Lets ChatVideoUI resolve per-camera display preferences
            // (mirror) from current device + facingMode. Not called for
            // screencast — its display is never mirrored.
            if (kind == StreamKind.Webcam)
                owner.OnWebcamTrackSettings(deviceId, facingMode);
        }
    }
}
