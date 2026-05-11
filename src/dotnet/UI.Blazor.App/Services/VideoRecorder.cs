using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;

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

    private AppUIHub Hub { get; }
    private Session Session => Hub.Session;
    private IJSRuntime JS => Hub.JS;
    private IAuthors Authors => Hub.Authors;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private ILiveVideoStreams LiveVideoStreams => Hub.LiveVideoStreams;
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public VideoSourceKind Kind { get; }
    public string DeviceId => _deviceId;
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
        // Always-on simulcast: JS startRecording builds the 3-tier ladder
        // (probe-gated to 2-tier on iOS HW-encoder budget exhaustion).
        await _jsRef.InvokeVoidAsync("startRecording", cancellationToken, chatId.Value, codecs).ConfigureAwait(false);
    }

    public async Task StartScreenCast(ChatId chatId, CancellationToken cancellationToken)
    {
        if (_startRequest.HasValue)
            throw StandardError.Constraint("Start request already set");
        _startRequest = (chatId, false);
        var codecs = await GetInitialAudienceCodecs(chatId).ConfigureAwait(false);
        await _jsRef.InvokeVoidAsync("startScreenCast", cancellationToken, chatId.Value, codecs).ConfigureAwait(false);
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

    public async Task<bool> SwitchFacing(CancellationToken cancellationToken)
    {
        // Clears _deviceId so a later state-sync SwitchCamera with a stale saved deviceId doesn't no-op.
        _deviceId = "";
        var result = await _jsRef
            .InvokeAsync<CameraSwitchResult>("switchFacing", cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
            return false;

        if (!string.IsNullOrEmpty(result.DeviceId))
            _deviceId = result.DeviceId;
        return true;
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

    private Task OnRecorderHealthSnapshot(RecorderHealthSnapshot snapshot)
    {
        var isDotNetConnected = !Hub.ConnectivityUI.IsConnected.IsValue(out var v) || v;
        var effectiveSnapshot = snapshot with {
            IsConnected = isDotNetConnected && snapshot.IsPeerConnected,
        };
        return Hub.VideoQualityUI.PushRecorderHealth(Kind, effectiveSnapshot, this, CancellationToken.None);
    }

    private async Task RunMaintenance(Task startTrigger, CancellationToken cancellationToken)
    {
        await startTrigger.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startRequest = GetStartRequest();
        var chatId = startRequest.Item1;
        var t1 = SubscribeToKeyFrameRequests(chatId, cancellationToken);
        var t2 = SubscribeToSupportedDecoderCodecs(chatId, cancellationToken);
        var t3 = ForwardRemoteStreamCount(chatId, cancellationToken);
        await Task.WhenAll(t1, t2, t3).ConfigureAwait(false);
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

    private sealed class RecorderCallbacks(ChatVideoUI owner, VideoRecorder videoRecorder, VideoSourceKind kind)
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
            // Fires from JS after a camera track is acquired (start or camera
            // switch). Lets ChatVideoUI resolve per-camera display preferences
            // (mirror) from current device + facingMode. Not called for
            // screencast — its display is never mirrored.
            if (kind == VideoSourceKind.Camera)
                owner.OnCameraTrackSettings(deviceId, facingMode);
        }

        [JSInvokable]
        public Task OnRecorderHealthSnapshot(
            double encodeRatioEma,
            double encodeRatioP90,
            double slotReplacementRateEma,
            double senderFrameDropRatioEma,
            double lastAckAgeMs,
            bool isPeerConnected,
            long floodGateSkipCount,
            long rpcStreamFramesSkipped,
            int senderQueueDepth,
            int senderMaxQueueDepth)
            => videoRecorder.OnRecorderHealthSnapshot(new RecorderHealthSnapshot(
                encodeRatioEma,
                encodeRatioP90,
                slotReplacementRateEma,
                senderFrameDropRatioEma,
                lastAckAgeMs,
                false,
                isPeerConnected,
                floodGateSkipCount,
                rpcStreamFramesSkipped,
                senderQueueDepth,
                senderMaxQueueDepth));
    }
}

public sealed record CameraSwitchResult(bool Success, string? DeviceId);
