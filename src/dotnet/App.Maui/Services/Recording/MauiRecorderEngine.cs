using System.Buffers;
using ActualChat.Audio;
using ActualChat.Streaming;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
using ActualLab.Rpc;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiRecorderEngine : IAudioRecorderEngine
{
    private static readonly TimeSpan RecordingFailedInterval = TimeSpan.FromMilliseconds(500);

    private readonly Lock _sync = new();
    private readonly Debouncer<Unit> _noSignalDetectedDebouncer;

    private ChatId? _chatId;
    private ChatEntryId? _repliedChatEntryId;
    private Channel<IMemoryOwner<byte>>? _currentStream;
    private CancellationTokenSource? _recordingCts;
    private Task? _processTask;
    private Task? _sendTask;
    private bool _isRecording;
    private bool _isConnected;
    private bool _isSignalDetected;
    private bool _isVoiceActive;
    private readonly UIHub _hub;

    private IAudioCapture AudioCapture => field ??= _hub.Services.GetRequiredService<IAudioCapture>();
    private ILiveAudioStreams LiveAudioStreams => field ??= _hub.Services.GetRequiredService<ILiveAudioStreams>();
    private VoiceActivityDetector VoiceActivityDetector => field ??= _hub.Services.GetRequiredService<VoiceActivityDetector>();
    private IAudioRecorderBackend AudioRecorderBackend => field ??= _hub.Services.GetRequiredService<IAudioRecorderBackend>();
    private IAudioCodec AudioCodec => field ??= _hub.Services.GetRequiredService<IAudioCodec>();
    private RecordingActivityClient RecordingActivityClient => field ??= _hub.Services.GetRequiredService<RecordingActivityClient>();
    private ConnectivityUI ConnectivityUI => field ??= _hub.Services.GetRequiredService<ConnectivityUI>();
    private MicrophonePermissionHandler MicrophonePermissionHandler => field ??= _hub.Services.GetRequiredService<MicrophonePermissionHandler>();
    private MomentClockSet Clocks => field ??= _hub.Clocks;
    private ILogger Log => field ??= _hub.LogFor<MauiRecorderEngine>();

    public MauiRecorderEngine(UIHub hub)
    {
        _hub = hub;
        _noSignalDetectedDebouncer = Debouncer.New<Unit>(
            hub.Clocks.CpuClock,
            RecordingFailedInterval,
            _ => { SetSignalDetected(false); return Task.CompletedTask; });
    }

    public async Task<bool> Start(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        CancellationToken cancellationToken = default)
    {
        // Stop any existing recording first
        await Stop(cancellationToken).ConfigureAwait(false);

        // No connectivity wait here on purpose: opening the mic doesn't need the network, and
        // CreateAudioStream - which does - still waits.

        // Create a new recording context
        var recordingCts = cancellationToken.CreateLinkedTokenSource();
        _recordingCts = recordingCts;
        var recordingToken = recordingCts.Token;

        // Mirror RPC peer connectivity into _isConnected for the duration of the
        // session so RecorderToggle's "Reconnecting..." indicator can light up
        // when the peer drops mid-recording (web-side parity). Sync initial
        // value first so InitializeRecordingState's notification carries it.
        SetConnected(ConnectivityUI.IsConnected.Value);
        _ = BackgroundTask.Run(
            () => WatchConnectivity(recordingToken),
            Log,
            "Failed to watch RPC connectivity",
            recordingToken);

        // Native capture setup can be non-trivial, and Start is usually called from
        // the Blazor/MAUI UI path. Keep it away from the main thread from the first
        // platform call, not only once frame processing begins.
        var microphoneStream = await BackgroundTask.Run(
                () => AudioCapture.Capture(recordingToken),
                Log,
                "Failed to initialize microphone capture",
                recordingToken)
            .ConfigureAwait(false);
        if (microphoneStream is null) {
            Log.LogWarning("Microphone stream is unavailable");
            var releasedCts = Interlocked.CompareExchange(ref _recordingCts, null, recordingCts);
            if (ReferenceEquals(releasedCts, recordingCts))
                recordingCts.CancelAndDisposeSilently();
            await SetRecording(false).ConfigureAwait(false);
            return false;
        }

        // Set the recording context
        SetRecordingContext(chatId, repliedChatEntryId);

        // Update state
        await InitializeRecordingState().ConfigureAwait(false);

        // Start processing in the background
        _processTask = BackgroundTask.Run(
            () => ProcessAudioStream(microphoneStream, recordingToken),
            Log,
            "Failed to process microphone stream",
            recordingToken);
        return true;
    }

    public async Task<bool> Stop(CancellationToken cancellationToken = default)
        => await Stop(true, cancellationToken).ConfigureAwait(false);

    private async Task<bool> Stop(bool waitForProcessTask, CancellationToken cancellationToken)
    {
        var recordingCts = Interlocked.Exchange(ref _recordingCts, null);
        if (recordingCts is not null)
            try {
                await recordingCts.CancelAsync().ConfigureAwait(false);
            }
            catch {
                // Best effort: Stop must proceed to draining the tasks.
            }

        // Both waits are bounded: Start() calls Stop() first, so a pipeline task that never
        // finishes used to block every later recording until StartRecordingTimeout gave up.
        var processTask = Interlocked.Exchange(ref _processTask, null);
        if (waitForProcessTask && processTask is not null)
            try {
                await processTask.WaitAsync(Constants.Audio.RecorderDrainTimeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) {
                Log.LogWarning("Stop: the audio processing task didn't finish in time, abandoning it");
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to process audio stream");
                throw;
            }

        var sendTask = Interlocked.Exchange(ref _sendTask, null);
        if (sendTask is not null)
            try {
                await sendTask.WaitAsync(Constants.Audio.RecorderDrainTimeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) {
                Log.LogWarning("Stop: the audio send task didn't finish in time, abandoning it");
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to send audio stream");
                throw;
            }

        CompleteAudioStream();
        ClearRecordingContext();

        await ResetRecordingState().ConfigureAwait(false);
        _noSignalDetectedDebouncer.Reset();
        recordingCts.DisposeSilently();
        return true;
    }

    public ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        VoiceActivityDetector.ConversationSignal();
        return ValueTask.CompletedTask;
    }

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var permissionStatus = await MicrophonePermissionHandler.Check(cancellationToken).ConfigureAwait(true);
        var lastVadEvent = VoiceActivityDetector.LastActivityEvent;

        bool isSignalDetected, isConnected;
        lock (_sync) {
            isSignalDetected = _isSignalDetected;
            isConnected = true; // RPC handles connectivity transparently
        }

        return new AudioRecorder.AudioDiagnosticsState {
            HasMicrophonePermission = permissionStatus,
            IsConnected = isConnected,
            HasMicrophoneStream = isSignalDetected,
            LastVadEvent = new AudioRecorder.VadEvent {
                Kind = lastVadEvent.Kind.ToString(),
                Offset = lastVadEvent.OffsetSeconds,
                Duration = lastVadEvent.DurationSeconds ?? 0,
                SpeechProb = lastVadEvent.SpeechProb,
            },
            IsSignalDetected = isSignalDetected,
            IsVadActive = VoiceActivityDetector.IsInitialized,
        };
    }

    #region State Management

    private void SetRecordingContext(ChatId chatId, ChatEntryId? repliedChatEntryId)
    {
        lock (_sync)
        {
            _chatId = chatId;
            _repliedChatEntryId = repliedChatEntryId;
        }
    }

    private void ClearRecordingContext()
    {
        lock (_sync)
        {
            _chatId = null;
            _repliedChatEntryId = null;
        }
    }

    private async Task InitializeRecordingState()
    {
        await SetRecording(true).ConfigureAwait(false);
        SetSignalDetected(false);
        await SetVoiceActive(false).ConfigureAwait(false);
    }

    private async Task ResetRecordingState()
    {
        await SetRecording(false).ConfigureAwait(false);
        await SetVoiceActive(false).ConfigureAwait(false);
        SetSignalDetected(false);
    }

    private ValueTask SetRecording(bool isRecording)
    {
        var previous = Interlocked.Exchange(ref _isRecording, isRecording);
        if (previous == isRecording)
            return ValueTask.CompletedTask;

        NotifyStateChange();
        return RecordingActivityClient.SetRecording(isRecording);
    }

    private void SetConnected(bool isConnected)
    {
        // Direct C# notify only — no JSI roundtrip. isConnected isn't part of the
        // JS RecordingActivity hub (Lit doesn't read it), and the JS AudioRecorderState
        // hub is web-only. Source of truth is ConnectivityUI, pushed through WatchConnectivity.
        var previous = Interlocked.Exchange(ref _isConnected, isConnected);
        if (previous == isConnected)
            return;

        NotifyStateChange();
    }

    private async Task WatchConnectivity(CancellationToken cancellationToken)
    {
        // ConnectivityUI.IsConnected is fed by PushIsConnectedToJS, which polls
        // RpcClientPeer.ConnectionState — i.e. it's a real-time signal, not a
        // one-shot flag. Mirror it into _isConnected for the recording session.
        var changes = ConnectivityUI.IsConnected.Computed.Changes(cancellationToken);
        await foreach (var change in changes.ConfigureAwait(false))
            SetConnected(change.Value);
    }

    private void SetSignalDetected(bool isSignalDetected)
    {
        // Direct C# notify only — no JSI roundtrip. The JS-side RecordingActivity hub
        // doesn't expose isSignalDetected (Lit components don't read it), and the JS
        // AudioRecorderState hub (which does) is web-only.
        if (isSignalDetected)
            _noSignalDetectedDebouncer.Debounce(Unit.Default);

        var previous = Interlocked.Exchange(ref _isSignalDetected, isSignalDetected);
        if (previous == isSignalDetected)
            return;

        NotifyStateChange();
    }

    private ValueTask SetVoiceActive(bool isVoiceActive)
    {
        var previous = Interlocked.Exchange(ref _isVoiceActive, isVoiceActive);
        if (previous == isVoiceActive)
            return ValueTask.CompletedTask;

        NotifyStateChange();
        return RecordingActivityClient.SetVoiceActive(isVoiceActive);
    }

    private void OnAudioPowerChange(double power)
    {
        var isVoiceActive = Volatile.Read(ref _isVoiceActive);
        if (isVoiceActive)
            RecordingActivityClient.SetAudioPower(power);
    }

    private void MicrophoneIsCaptured()
        // Direct C# notify only — no JSI roundtrip. The heartbeat/microphoneIsCaptured
        // signal lives on the JS AudioRecorderState hub, which is web-only — on MAUI
        // we don't go through that JS relay at all (C# already knows everything directly).
        => SetSignalDetected(true);

    private void NotifyStateChange()
    {
        bool isRecording, isSignalDetected, isConnected, isVoiceActive;
        lock (_sync) {
            isRecording = _isRecording;
            isSignalDetected = _isSignalDetected;
            isConnected = _isConnected;
            isVoiceActive = _isVoiceActive;
        }
        AudioRecorderBackend.OnRecordingStateChange(isRecording, isSignalDetected, isConnected, isVoiceActive);
    }

    #endregion

    #region Audio Processing

    private async Task ProcessAudioStream(IAsyncEnumerable<IMemoryOwner<float>> frames, CancellationToken cancellationToken)
    {
        try {
            var processor = new AudioStreamProcessor(this, Log);
            await processor.Process(frames, cancellationToken).ConfigureAwait(false);
        }
        finally {
            await ResetRecordingState().ConfigureAwait(false);
        }
    }

    private async Task RecordingHeartbeat(CancellationToken cancellationToken)
    {
        try {
            ChatId? chatId;
            bool isRecording;
            lock (_sync) {
                chatId = _chatId;
                isRecording = _isRecording;
            }
            if (!isRecording)
                return;

            if (chatId is null) {
                await Stop(false, cancellationToken).ConfigureAwait(false);
                return;
            }

            var backendRecording = AudioRecorderBackend.IsRecording(chatId.Value);
            if (backendRecording)
                return;

            await Stop(false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to run recording heartbeat");
            await Stop(false, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    #endregion

    #region AudioStreamProcessor

    private async Task SendAudio(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        int preSkip,
        Moment firstFrameSourceCapturedAt,
        ChannelReader<IMemoryOwner<byte>> packetReader,
        CancellationToken cancellationToken)
    {
        // Mirrors the TS AudioStream.stream() retry loop: PushAudio is configured with
        // RpcRemoteExecutionMode.AwaitForConnection | AllowReconnect (see ILiveAudioStreams.cs),
        // so same-peer WS reconnects resume transparently via $sys.Reconnect. On peer-change
        // the sender is disposed and PushAudio throws; we create a new PushAudio call (a new
        // server-side chat entry) and continue draining the channel from where we stopped.
        //
        // The channel itself is the capture-side buffer — packets produced while disconnected
        // sit in it (BoundedChannelFullMode.Wait applies backpressure to the encoder), and
        // the next iteration picks them up. Frames that were in the stream sender's
        // un-ACKed window at peer-change are lost (the old server-side entry ends there);
        // everything in the channel is preserved.
        var session = _hub.Session;

        while (!cancellationToken.IsCancellationRequested) {
            // Per-call state: each iteration is a fresh PushAudio = fresh chat entry,
            // so packetIndex (→ frame Offset) and sourceStartOffset reset.
            var packetIndex = 0;
            var sourceStartOffsetSeconds = firstFrameSourceCapturedAt.EpochOffset.TotalSeconds;

            // Cadence tracking: warn when wall-clock between successful yields drifts
            // above 60ms (3+ frames of expected 20ms pace). Bursty send → all listeners
            // hear simultaneous "кваканье" on the live path; replay stays clean because
            // server stores frames with correct packetIndex*20ms offsets.
            // Also track:
            //   - max channel queue depth (`packetReader.Count`) sampled at yield time:
            //     queue grows under encode-faster-than-send (RPC stalls); queue near-zero
            //     while cadence gaps → encoder is the bottleneck, not send.
            //   - GC collections per window: gen0/1/2 deltas surface GC-pause culprits.
            var lastSendStamp = 0L;
            var lastSendLogStamp = Stopwatch.GetTimestamp();
            var sendGapsInWindow = 0;
            var sendMaxGapMs = 0.0;
            var sendMaxQueueDepth = 0;
            var canCount = packetReader.CanCount;
            var sendGen0Start = GC.CollectionCount(0);
            var sendGen1Start = GC.CollectionCount(1);
            var sendGen2Start = GC.CollectionCount(2);

            async IAsyncEnumerable<AudioFrame> BuildFrames(
                [EnumeratorCancellation] CancellationToken callCancellationToken = default)
            {
                while (await packetReader.WaitToReadAsync(callCancellationToken).ConfigureAwait(false)) {
                    while (packetReader.TryRead(out var packet)) {
                        using var _ = packet;
                        if (canCount) {
                            var depth = packetReader.Count;
                            if (depth > sendMaxQueueDepth)
                                sendMaxQueueDepth = depth;
                        }
                        yield return new AudioFrame {
                            Data = packet.Memory.ToArray(),
                            Offset = TimeSpan.FromMilliseconds(packetIndex * Constants.Audio.OpusFrameDurationMs),
                            Duration = Constants.Audio.OpusFrameDuration,
                        };
                        packetIndex++;

                        var nowStamp = Stopwatch.GetTimestamp();
                        if (lastSendStamp != 0) {
                            var deltaMs = Stopwatch.GetElapsedTime(lastSendStamp, nowStamp).TotalMilliseconds;
                            if (deltaMs > 60.0) {
                                sendGapsInWindow++;
                                if (deltaMs > sendMaxGapMs)
                                    sendMaxGapMs = deltaMs;
                            }
                        }
                        lastSendStamp = nowStamp;

                        if (Stopwatch.GetElapsedTime(lastSendLogStamp, nowStamp).TotalSeconds >= 1.0) {
                            var gen0 = GC.CollectionCount(0) - sendGen0Start;
                            var gen1 = GC.CollectionCount(1) - sendGen1Start;
                            var gen2 = GC.CollectionCount(2) - sendGen2Start;
                            if (sendGapsInWindow > 0)
                                Log.LogWarning(
                                    "send-cadence: {Gaps} gap(s) >60ms in last second; max gap {MaxMs:F0}ms; max queue depth {QueueDepth}; gc 0/1/2={Gen0}/{Gen1}/{Gen2}",
                                    sendGapsInWindow, sendMaxGapMs, sendMaxQueueDepth, gen0, gen1, gen2);
                            sendGapsInWindow = 0;
                            sendMaxGapMs = 0;
                            sendMaxQueueDepth = 0;
                            sendGen0Start += gen0; sendGen1Start += gen1; sendGen2Start += gen2;
                            lastSendLogStamp = nowStamp;
                        }
                    }
                }
            }

            var frameStream = BuildFrames(cancellationToken).SuppressCancellation(cancellationToken);
            try {
                var rpcStream = RpcStream.New(frameStream);
                await LiveAudioStreams.PushStream(
                    session,
                    chatId.Value,
                    repliedChatEntryId?.Value,
                    sourceStartOffsetSeconds,
                    preSkip,
                    rpcStream,
                    cancellationToken).ConfigureAwait(false);
                // Natural completion: channel writer was completed (CompleteAudioStream),
                // reader drained, BuildFrames returned, PushAudio finished cleanly.
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                // Peer change (RpcException "reconnected to a different peer and AllowResend is
                // not set") or any other transient failure — retry with a fresh call.
                // The channel still holds whatever packets weren't yet consumed, and the encoder
                // keeps writing new ones.
                Log.LogWarning(e, "PushAudio call ended ({SentFrames} frames), retrying with a new call", packetIndex);
                repliedChatEntryId = null; // Only the initial segment carries the reply target.
                // Short delay to avoid tight-looping on a persistent failure; the recording CT
                // cancels this delay when the user stops recording.
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ChannelWriter<IMemoryOwner<byte>>?> CreateAudioStream(Moment firstFrameSourceCapturedAt, CancellationToken cancellationToken)
    {
        ChatId? chatId;
        ChatEntryId? repliedChatEntryId;

        lock (_sync) {
            chatId = _chatId;
            repliedChatEntryId = _repliedChatEntryId;
        }

        if (chatId is null)
            return null;

        // Bounded like the one in Start: unbounded here means a connectivity signal that never
        // arrives swallows the whole utterance with nothing logged.
        try {
            await ConnectivityUI.WhenConnected(cancellationToken)
                .WaitAsync(Constants.Audio.ConnectivityWaitTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning("CreateAudioStream: no connectivity signal yet, streaming anyway");
        }

        var stream = Channel.CreateBounded<IMemoryOwner<byte>>(
            new BoundedChannelOptions(Constants.Audio.StreamingChannelCapacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });

        // TODO(AK): Specify PreSkip
        _sendTask = SendAudio(chatId, repliedChatEntryId, 0, firstFrameSourceCapturedAt, stream.Reader, cancellationToken);
        lock (_sync) {
            _currentStream = stream;
            _repliedChatEntryId = null; // Clear so it's only used once
        }
        return stream.Writer;
    }

    private void CompleteAudioStream()
    {
        var stream = Interlocked.Exchange(ref _currentStream, null);
        if (stream is null)
            return;

        stream.Writer.TryComplete();
        while (stream.Reader.TryRead(out _)) {
            // Drain remaining frames so GC can reclaim them; AudioFrame is no longer IDisposable.
        }
    }

    // Separate class to handle the complex audio processing logic
    private sealed class AudioStreamProcessor(MauiRecorderEngine engine, ILogger log)
    {
        private readonly VoiceActivityDetector _vad = engine.VoiceActivityDetector;
        private readonly IAudioCodec _audioCodec = engine.AudioCodec;

        private readonly BlockRingBuffer<float> _vadBuffer = new (Constants.Audio.RecordingSampleRate * 2); // 2s
        private readonly BlockRingBuffer<float> _encodingBuffer = new (Constants.Audio.RecordingSampleRate / 2); // 500ms
        private readonly Queue<Moment> _encodingFrameSourceCapturedAts = new();

        private FuncWorker? _encodeSendWorker;
        private bool _voiceActive;

        public async Task Process(
            IAsyncEnumerable<IMemoryOwner<float>> frames,
            CancellationToken cancellationToken)
        {
            const int samplesPerRecordingInProgressCall = Constants.Audio.RecordingSampleRate / 1000 * 200; // 200ms
            const int gainFrameLen = Constants.Audio.VadFrameLength * 3;
            using var gainBuffer = new BlockRingBuffer<float>(Constants.Audio.OpusFrameLength * 10); // 200ms
            using var gainFrameOwner = ArrayPools.SharedFloatPool.LeaseArrayOwner(gainFrameLen, true);

            await _vad.EnsureInitialized(cancellationToken).ConfigureAwait(false);
            _vad.Reset();
            var samplesSinceLastReport = samplesPerRecordingInProgressCall;
            var hasEverHadSignal = false;
            // Cadence on the Process loop body itself: if iterations stall >60ms,
            // the bottleneck is upstream of the encoder (encoder logs already show
            // queue=0, encode-call ≤3ms — so it waits on us). Localizes whether
            // UI-hub awaits / VAD inference / heartbeat are blocking the pipeline.
            var lastIterStamp = 0L;
            var lastIterLogStamp = Stopwatch.GetTimestamp();
            var iterGapsInWindow = 0;
            var iterMaxGapMs = 0.0;
            // Heartbeat throttle: 1Hz instead of 50Hz. The heartbeat checks if the
            // backend still expects this recording to be active — sub-second
            // resolution is fine and we save 49 awaits/sec on the audio hot path.
            var lastHeartbeatStamp = Stopwatch.GetTimestamp();
            try {
                await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    using var _1 = frame;
                    var memory = frame.Memory;
                    var iterStamp = Stopwatch.GetTimestamp();
                    if (lastIterStamp != 0) {
                        var deltaMs = Stopwatch.GetElapsedTime(lastIterStamp, iterStamp).TotalMilliseconds;
                        if (deltaMs > 60.0) {
                            iterGapsInWindow++;
                            if (deltaMs > iterMaxGapMs)
                                iterMaxGapMs = deltaMs;
                        }
                    }

                    // Notify that microphone is captured (UI-only, sync — no JSI hop:
                    // the heartbeat lives on the web-only JS AudioRecorderState hub, which MAUI bypasses).
                    // A withheld microphone still delivers frames, just all-zero ones, so counting
                    // frames alone would report a healthy signal for one that records silence.
                    hasEverHadSignal = hasEverHadSignal || HasSignal(memory.Span);
                    samplesSinceLastReport += memory.Length;
                    if (samplesSinceLastReport >= samplesPerRecordingInProgressCall) {
                        samplesSinceLastReport = 0;
                        if (hasEverHadSignal)
                            engine.MicrophoneIsCaptured();
                    }

                    // Process the audio frame (sync — returns Task.CompletedTask)
                    await ProcessAudioFrame(memory, cancellationToken).ConfigureAwait(false);

                    // Audio level meter update — sync submit into a coalescer; the JSI
                    // call runs detached, intermediate values drop if JS lags.
                    // gainBuffer self-throttles to ~96ms (32ms * 3), so it's not 50Hz here.
                    gainBuffer.TryWrite(memory.Span);
                    if (gainBuffer.TryRead(gainFrameOwner.Span, out _)) {
                        var gain = AudioExt.ApproximateGain(gainFrameOwner.Span);
                        engine.OnAudioPowerChange(gain);
                    }

                    // Process VAD events (must stay awaited — drives encode start/end)
                    await ProcessVadEvents(cancellationToken).ConfigureAwait(false);

                    // Heartbeat — throttled to ~1Hz. Loss of <1s reactivity to backend
                    // requesting stop is acceptable for an audio-quality tradeoff.
                    if (Stopwatch.GetElapsedTime(lastHeartbeatStamp, iterStamp).TotalSeconds >= 1.0) {
                        lastHeartbeatStamp = iterStamp;
                        await engine.RecordingHeartbeat(cancellationToken).ConfigureAwait(false);
                    }

                    lastIterStamp = iterStamp;
                    if (Stopwatch.GetElapsedTime(lastIterLogStamp, iterStamp).TotalSeconds >= 1.0) {
                        if (iterGapsInWindow > 0)
                            log.LogWarning(
                                "process-cadence: {Gaps} iter(s) >60ms in last second; max gap {MaxMs:F0}ms",
                                iterGapsInWindow, iterMaxGapMs);
                        iterGapsInWindow = 0;
                        iterMaxGapMs = 0;
                        lastIterLogStamp = iterStamp;
                    }
                }
            }
            finally {
                await StopEncodeSendWorker().ConfigureAwait(false);
            }
        }

        private Task ProcessAudioFrame(ReadOnlyMemory<float> frame, CancellationToken cancellationToken)
        {
            // Push frame to buffers
            var data = frame.Span;
            var isVoiceActive = _voiceActive;
            var sourceCapturedAt = engine.Clocks.ServerClock.Now
                - TimeSpan.FromSeconds((double)data.Length / Constants.Audio.RecordingSampleRate);

            // Push to VAD buffer (fire-and-forget: drop if full)
            _vadBuffer.TryWrite(data);

            // Push to encoding buffer
            if (isVoiceActive) {
                if (_encodingBuffer.TryWrite(data))
                    _encodingFrameSourceCapturedAts.Enqueue(sourceCapturedAt);
            }
            else {
                if (!_encodingBuffer.TryWrite(data)) {
                    // Trim oldest audio to maintain preroll
                    var retryCount = 0;
                    var pushed = false;
                    var skipBuf = new float[Constants.Audio.OpusFrameLength];
                    while (!pushed && retryCount++ < 32) {
                        if (!_encodingBuffer.TryRead(skipBuf, out _))
                            break;
                        _encodingFrameSourceCapturedAts.TryDequeue(out _);

                        pushed = _encodingBuffer.TryWrite(data);
                    }
                    if (pushed)
                        _encodingFrameSourceCapturedAts.Enqueue(sourceCapturedAt);
                }
                else
                    _encodingFrameSourceCapturedAts.Enqueue(sourceCapturedAt);
            }
            return Task.CompletedTask;
        }

        private async Task ProcessVadEvents(CancellationToken cancellationToken)
        {
            // Process VAD events in batches to reduce CPU usage
            const int vadChunkSize = Constants.Audio.VadFrameLength * 3;
            using var vadFrameOwner = ArrayPools.SharedFloatPool.LeaseArrayOwner(vadChunkSize, true);
            while (true) {
                var vadFrame = vadFrameOwner.Span;
                if (!_vadBuffer.TryRead(vadFrame, out _))
                    break;

                var vadResults = _vad.AppendChunk(vadFrame);

                foreach (var vadResult in vadResults)
                    if (vadResult.HasEvent)
                        await HandleVadEvent(vadResult.Change!.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task HandleVadEvent(VoiceActivityChange change, CancellationToken cancellationToken)
        {
            log.LogInformation("VAD: {Kind}", change.Kind);
            if (change.Kind == VoiceActivityKind.Start) {
                if (_voiceActive) return;

                // Trim pre-roll: read all buffered audio, find speech onset, keep only from there
                var firstFrameSourceCapturedAt = TrimPreRollBuffer();

                var stream = await engine.CreateAudioStream(firstFrameSourceCapturedAt, cancellationToken).ConfigureAwait(false);
                if (stream == null) {
                    log.LogWarning("VAD: voice started but no audio stream could be created");
                    return;
                }

                _encodeSendWorker = FuncWorker.Start(ct => EncodeAndSend(stream, ct), cancellationToken);
                _voiceActive = true;
                await engine.SetVoiceActive(true).ConfigureAwait(false);
            }
            else // VoiceActivityKind.End
            {
                if (!_voiceActive) return;

                _voiceActive = false;
                await engine.SetVoiceActive(false).ConfigureAwait(false);
                await StopEncodeSendWorker().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Trims the encoding buffer pre-roll by discarding silent frames before speech onset.
        /// Mirrors the Web's opus-encoder-worker processQueue('in') trimming logic.
        /// Returns the source timestamp of the first frame remaining in the buffer after trimming.
        /// </summary>
        private Moment TrimPreRollBuffer()
        {
            const int frameLen = Constants.Audio.OpusFrameLength; // 320 samples = 20ms
            const int preRollFrameLimit = Constants.Audio.VoicePreRollFrameLimit;

            var bufferedSamples = _encodingBuffer.Count;
            var fallbackSourceCapturedAt = engine.Clocks.ServerClock.Now
                - TimeSpan.FromSeconds((double)bufferedSamples / Constants.Audio.RecordingSampleRate);
            if (bufferedSamples < frameLen)
                return _encodingFrameSourceCapturedAts.TryPeek(out var sourceCapturedAt)
                    ? sourceCapturedAt
                    : fallbackSourceCapturedAt;

            // Read all buffered audio into a temporary array
            var frameCount = bufferedSamples / frameLen;
            var totalSamples = frameCount * frameLen;
            var tempBuffer = new float[totalSamples];
            var gains = new double[frameCount];
            var sourceCapturedAts = new Moment[frameCount];

            // Read frame by frame and compute gains
            for (int i = 0; i < frameCount; i++) {
                var frameSpan = tempBuffer.AsSpan(i * frameLen, frameLen);
                if (!_encodingBuffer.TryRead(frameSpan, out _))
                    break;
                sourceCapturedAts[i] = _encodingFrameSourceCapturedAts.TryDequeue(out var sourceCapturedAt)
                    ? sourceCapturedAt
                    : fallbackSourceCapturedAt + TimeSpan.FromMilliseconds(i * Constants.Audio.OpusFrameDurationMs);
                gains[i] = AudioExt.ApproximateGain(frameSpan);
            }

            // Calculate average speech gain from the speech-confirmed tail of the pre-roll window.
            var retainedFrameLimit = Math.Min(preRollFrameLimit, frameCount);
            var speechFrames = Math.Max(
                1,
                Math.Min(retainedFrameLimit, (int)Math.Ceiling(preRollFrameLimit / 3d)));
            double speechGain = 0;
            for (int i = frameCount - speechFrames; i < frameCount; i++)
                speechGain += gains[i];
            speechGain /= speechFrames;

            // Scan backward within the pre-roll window to find where gain drops below speechGain/2.
            var threshold = speechGain / 2;
            var minStartIndex = frameCount - retainedFrameLimit;
            var startIndex = frameCount - speechFrames - 1;
            while (startIndex >= minStartIndex) {
                if (gains[startIndex] < threshold)
                    break;
                startIndex--;
            }

            // Discard older frames, but retain no more than the configured pre-roll window.
            var framesToDiscard = startIndex >= minStartIndex
                ? startIndex - 1
                : minStartIndex;
            framesToDiscard = Math.Clamp(framesToDiscard, minStartIndex, frameCount - speechFrames);
            var framesToKeep = frameCount - framesToDiscard;

            // Write remaining frames back to encoding buffer
            _encodingBuffer.Clear();
            var keepStart = framesToDiscard * frameLen;
            var keepLength = framesToKeep * frameLen;
            _encodingBuffer.TryWrite(tempBuffer.AsSpan(keepStart, keepLength));
            for (var i = framesToDiscard; i < frameCount; i++)
                _encodingFrameSourceCapturedAts.Enqueue(sourceCapturedAts[i]);

            return _encodingFrameSourceCapturedAts.TryPeek(out var firstSourceCapturedAt)
                ? firstSourceCapturedAt
                : fallbackSourceCapturedAt;
        }

        private Task StopEncodeSendWorker()
            => Interlocked.Exchange(ref _encodeSendWorker, null) is { } worker
                ? BackgroundTask.Run(worker.Stop, log, "Failed to stop encode/send worker")
                : Task.CompletedTask;

        private async IAsyncEnumerable<IMemoryOwner<float>> ReadEncodingFrames(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            const int frameLen = Constants.Audio.OpusFrameLength;
            while (!cancellationToken.IsCancellationRequested) {
                // We need to return exactly frameLen samples
                var owner = ArrayPools.SharedFloatPool.LeaseArrayOwner(frameLen, true);
                if (!_encodingBuffer.TryRead(owner.Span, out var whenReady)) {
                    owner.Dispose();
                    await whenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                _encodingFrameSourceCapturedAts.TryDequeue(out _);
                yield return owner;
            }
        }

        private async Task EncodeAndSend(ChannelWriter<IMemoryOwner<byte>> stream, CancellationToken cancellationToken)
        {
            try {
                var frames = ReadEncodingFrames(cancellationToken);
                await foreach (var packet in _audioCodec.Encode(frames, cancellationToken).ConfigureAwait(false)) {
                    if (packet.Memory.Length == 0)
                        continue;

                    if (!stream.TryWrite(packet))
                        await stream.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) {
                // Expected when stopping
            }
            catch (Exception e) {
                log.LogError(e, "Failed to encode/send audio");
            }
            finally {
                engine.CompleteAudioStream();
            }
        }

        private static bool HasSignal(ReadOnlySpan<float> samples)
        {
            foreach (var sample in samples)
                if (sample != 0f)
                    return true;

            return false;
        }
    }
    #endregion
}
