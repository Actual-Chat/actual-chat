using System.Buffers;
using ActualChat.Audio;
using ActualChat.Streaming;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
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
    private Task? _sendTask;
    private bool _isRecording;
    private bool _isConnected;
    private bool _isSignalDetected;
    private bool _isVoiceActive;
    private readonly UIHub _hub;

    private IStreamClient StreamClient => field ??= _hub.Services.GetRequiredService<IStreamClient>();
    private MicrophonePermissionHandler MicrophonePermissionHandler => field ??= _hub.Services.GetRequiredService<MicrophonePermissionHandler>();
    private IAudioCapture AudioCapture => field ??= _hub.Services.GetRequiredService<IAudioCapture>();
    private VoiceActivityDetector VoiceActivityDetector => field ??= _hub.Services.GetRequiredService<VoiceActivityDetector>();
    private IAudioRecorderBackend AudioRecorderBackend => field ??= _hub.Services.GetRequiredService<IAudioRecorderBackend>();
    private IAudioCodec AudioCodec => field ??= _hub.Services.GetRequiredService<IAudioCodec>();
    private RecorderStateHub RecorderStateHub => field ??= _hub.Services.GetRequiredService<RecorderStateHub>();
    private ConnectivityUI ConnectivityUI => field ??= _hub.Services.GetRequiredService<ConnectivityUI>();
    private MomentClockSet Clocks => field ??= _hub.Clocks;
    private ILogger Log => field ??= _hub.LogFor<MauiRecorderEngine>();

    public MauiRecorderEngine(UIHub hub)
    {
        _hub = hub;
        _noSignalDetectedDebouncer = Debouncer.New<Unit>(
            hub.Clocks.CpuClock,
            RecordingFailedInterval,
            _ => SetSignalDetected(false).AsTask());
    }

    public async Task<bool> Start(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        CancellationToken cancellationToken = default)
    {
        // Stop any existing recording first
        await Stop(cancellationToken).ConfigureAwait(false);

        // Wait for online before starting
        await ConnectivityUI.WhenConnected(cancellationToken).ConfigureAwait(false);
        await SetConnected(true).ConfigureAwait(false);

        // Create a new recording context
        _recordingCts = cancellationToken.CreateLinkedTokenSource();
        var recordingToken = _recordingCts.Token;

        var microphoneStream = await AudioCapture.Capture(recordingToken).ConfigureAwait(false);
        if (microphoneStream is null) {
            Log.LogWarning("Microphone stream is unavailable");
            await SetRecording(false).ConfigureAwait(false);
            return false;
        }

        // Set the recording context
        SetRecordingContext(chatId, repliedChatEntryId);

        // Update state
        await InitializeRecordingState().ConfigureAwait(false);

        // Start processing in the background
        _ = BackgroundTask.Run(
            () => ProcessAudioStream(microphoneStream, recordingToken),
            Log, "Failed to process microphone stream", recordingToken);
        return true;
    }

    public async Task<bool> Stop(CancellationToken cancellationToken = default)
    {
        var recordingCts = Interlocked.Exchange(ref _recordingCts, null);
        recordingCts.CancelAndDisposeSilently();

        var sendTask = Interlocked.Exchange(ref _sendTask, null);
        if (sendTask != null)
            try {
                await sendTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) {
                Log.LogError(e, "Failed to send audio stream");
                throw;
            }
        CompleteAudioStream();
        ClearRecordingContext();

        await ResetRecordingState().ConfigureAwait(false);
        _noSignalDetectedDebouncer.Reset();
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
        await SetSignalDetected(false).ConfigureAwait(false);
        await SetVoiceActive(false).ConfigureAwait(false);
    }

    private async Task ResetRecordingState()
    {
        await SetRecording(false).ConfigureAwait(false);
        await SetVoiceActive(false).ConfigureAwait(false);
        await SetSignalDetected(false).ConfigureAwait(false);
    }

    private ValueTask SetRecording(bool isRecording)
    {
        var previous = Interlocked.Exchange(ref _isRecording, isRecording);
        if (previous == isRecording)
            return ValueTask.CompletedTask;

        NotifyStateChange();
        return RecorderStateHub.SetRecording(isRecording);
    }

    private ValueTask SetConnected(bool isConnected)
    {
        var previous = Interlocked.Exchange(ref _isConnected, isConnected);
        if (previous == isConnected)
            return ValueTask.CompletedTask;

        NotifyStateChange();
        return RecorderStateHub.SetConnected(isConnected);
    }

    private ValueTask SetSignalDetected(bool isSignalDetected)
    {
        if (isSignalDetected)
            _noSignalDetectedDebouncer.Debounce(Unit.Default);

        var previous = Interlocked.Exchange(ref _isSignalDetected, isSignalDetected);
        if (previous == isSignalDetected)
            return ValueTask.CompletedTask;

        NotifyStateChange();
        return RecorderStateHub.SetSignalDetected(isSignalDetected);
    }

    private ValueTask SetVoiceActive(bool isVoiceActive)
    {
        var previous = Interlocked.Exchange(ref _isVoiceActive, isVoiceActive);
        if (previous == isVoiceActive)
            return ValueTask.CompletedTask;

        NotifyStateChange();
        return RecorderStateHub.SetVoiceActive(isVoiceActive);
    }

    private async ValueTask OnAudioPowerChange(double power)
    {
        var isVoiceActive = Volatile.Read(ref _isVoiceActive);
        if (isVoiceActive)
            await RecorderStateHub.OnAudioPowerChange(power).ConfigureAwait(false);
    }

    private async ValueTask MicrophoneIsCaptured()
    {
        await SetSignalDetected(true).ConfigureAwait(false);
        await RecorderStateHub.MicrophoneIsCaptured().ConfigureAwait(false);
    }

    private void NotifyStateChange()
    {
        bool isRecording, isSignalDetected, isConnected, isVoiceActive;
        lock (_sync)
        {
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
                await Stop(cancellationToken).ConfigureAwait(false);
                return;
            }

            var backendRecording = AudioRecorderBackend.IsRecording(chatId.Value);
            if (backendRecording)
                return;

            await Stop(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to run recording heartbeat");
            await Stop(cancellationToken).ConfigureAwait(false);
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
        // RpcRemoteExecutionMode.AwaitForConnection | AllowReconnect (see IStreamServer.cs),
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

            async IAsyncEnumerable<AudioFrame> BuildFrames(
                [EnumeratorCancellation] CancellationToken callCancellationToken = default)
            {
                while (await packetReader.WaitToReadAsync(callCancellationToken).ConfigureAwait(false)) {
                    while (packetReader.TryRead(out var packet)) {
                        using var _ = packet;
                        yield return new AudioFrame {
                            Data = packet.Memory.ToArray(),
                            Offset = TimeSpan.FromMilliseconds(packetIndex * Constants.Audio.OpusFrameDurationMs),
                            Duration = Constants.Audio.OpusFrameDuration,
                        };
                        packetIndex++;
                    }
                }
            }

            var frameStream = BuildFrames(cancellationToken).SuppressCancellation(cancellationToken);
            try {
                await StreamClient.PushAudio(
                    session,
                    chatId.Value,
                    repliedChatEntryId?.Value,
                    sourceStartOffsetSeconds,
                    preSkip,
                    frameStream,
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

        await ConnectivityUI.WhenConnected(cancellationToken).ConfigureAwait(false);
        await SetConnected(true).ConfigureAwait(false);

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
            try {
                await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    using var _1 = frame;
                    var memory = frame.Memory;

                    // Notify that microphone is captured
                    samplesSinceLastReport += memory.Length;
                    if (samplesSinceLastReport >= samplesPerRecordingInProgressCall) {
                        samplesSinceLastReport = 0;
                        await engine.MicrophoneIsCaptured().ConfigureAwait(false);
                    }

                    // Process the audio frame
                    await ProcessAudioFrame(memory, cancellationToken).ConfigureAwait(false);

                    // Throttle microphone capture notifications by 32ms * 3 = 96ms
                    gainBuffer.TryWrite(memory.Span);
                    if (gainBuffer.TryRead(gainFrameOwner.Span, out _)) {
                        var gain = AudioExt.ApproximateGain(gainFrameOwner.Span);
                        await engine.OnAudioPowerChange(gain).ConfigureAwait(false);
                    }

                    // Process VAD events
                    await ProcessVadEvents(cancellationToken).ConfigureAwait(false);

                    // Heartbeat check
                    await engine.RecordingHeartbeat(cancellationToken).ConfigureAwait(false);
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
            if (change.Kind == VoiceActivityKind.Start) {
                if (_voiceActive) return;

                // Trim pre-roll: read all buffered audio, find speech onset, keep only from there
                var firstFrameSourceCapturedAt = TrimPreRollBuffer();

                var stream = await engine.CreateAudioStream(firstFrameSourceCapturedAt, cancellationToken).ConfigureAwait(false);
                if (stream == null) return;

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
            const int minKeepFrames = 5; // Keep at least 100ms before speech

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

            // Calculate average speech gain from last 5 frames (the speech onset region)
            var speechFrames = Math.Min(5, frameCount);
            double speechGain = 0;
            for (int i = frameCount - speechFrames; i < frameCount; i++)
                speechGain += gains[i];
            speechGain /= speechFrames;

            // Scan backward from end to find where gain drops below speechGain/20
            var threshold = speechGain / 20;
            var startIndex = frameCount - 2;
            while (startIndex > 0) {
                if (gains[startIndex] < threshold)
                    break;
                startIndex--;
            }

            // Discard frames before startIndex, but keep at least minKeepFrames
            var framesToDiscard = Math.Max(0, startIndex - 1);
            var framesToKeep = frameCount - framesToDiscard;
            framesToKeep = Math.Max(framesToKeep, Math.Min(minKeepFrames, frameCount));
            framesToDiscard = frameCount - framesToKeep;

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
    }
    #endregion
}
