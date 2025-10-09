using System.Buffers;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiRecorderEngine : IAudioRecorderEngine
{
    private static readonly TimeSpan RecordingFailedInterval = TimeSpan.FromMilliseconds(500);

    private readonly Lock _sync = new();
    private readonly AudioStreamer _streamer;
    private readonly Debouncer<Unit> _noSignalDetectedDebouncer;

    private ChatId? _chatId;
    private string? _sessionToken;
    private ChatEntryId? _repliedChatEntryId;
    private Channel<IMemoryOwner<byte>>? _currentStream;
    private CancellationTokenSource? _recordingCts;
    private Task? _sendTask;
    private bool _isRecording;
    private bool _isConnected;
    private bool _isSignalDetected;
    private bool _isVoiceActive;
    private readonly UIHub _hub;

    [field: AllowNull] [field: MaybeNull]
    private MicrophonePermissionHandler MicrophonePermissionHandler => field ??= _hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    [field: AllowNull] [field: MaybeNull]
    private IAudioCapture AudioCapture => field ??= _hub.Services.GetRequiredService<IAudioCapture>();

    [field: AllowNull] [field: MaybeNull]
    private VoiceActivityDetector VoiceActivityDetector => field ??= _hub.Services.GetRequiredService<VoiceActivityDetector>();

    [field: AllowNull] [field: MaybeNull]
    private IAudioRecorderBackend AudioRecorderBackend => field ??= _hub.Services.GetRequiredService<IAudioRecorderBackend>();

    [field: AllowNull] [field: MaybeNull]
    private IAudioCodec AudioCodec => field ??= _hub.Services.GetRequiredService<IAudioCodec>();

    [field: AllowNull] [field: MaybeNull]
    private ILogger Log => field ??= _hub.LogFor<MauiRecorderEngine>();

    [field: AllowNull] [field: MaybeNull]
    private RecorderStateHub RecorderStateHub => field ??= _hub.Services.GetRequiredService<RecorderStateHub>();

    public MauiRecorderEngine(UIHub hub)
    {
        _hub = hub;
        _streamer = new AudioStreamer(hub);
        _noSignalDetectedDebouncer = Debouncer.New<Unit>(
            hub.Clocks.CoarseCpuClock,
            RecordingFailedInterval,
            _ => SetSignalDetected(false).AsTask());
    }

    public async Task<bool> Start(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        // Stop any existing recording first
        await Stop(cancellationToken).ConfigureAwait(false);

        // Initialize and connect AudioStreamer
        await _streamer.EnsureConnected(true, cancellationToken).ConfigureAwait(false);
        await SetConnected(true).ConfigureAwait(false);

        // Create a new recording context
        _recordingCts = cancellationToken.CreateLinkedTokenSource();
        var token = _recordingCts.Token;

        var microphoneStream = await AudioCapture.Capture(token).ConfigureAwait(false);
        if (microphoneStream is null) {
            Log.LogWarning("Microphone stream is unavailable");
            await SetRecording(false).ConfigureAwait(false);
            return false;
        }

        // Set the recording context
        SetRecordingContext(chatId, sessionToken, repliedChatEntryId);

        // Update state
        await InitializeRecordingState().ConfigureAwait(false);

        // Start processing in the background
        _ = BackgroundTask.Run(() => ProcessAudioStream(microphoneStream, token),
            Log,
            "Failed to process microphone stream",
            token);
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

    public async ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
        => await _streamer.EnsureConnected(quickReconnect, cancellationToken).ConfigureAwait(false);

    public ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        VoiceActivityDetector.ConversationSignal();
        return ValueTask.CompletedTask;
    }

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var permissionStatus = await MicrophonePermissionHandler.Check(cancellationToken);
        var lastVadEvent = VoiceActivityDetector.LastActivityEvent;

        bool isSignalDetected, isConnected;
        lock (_sync) {
            isSignalDetected = _isSignalDetected;
            isConnected = _streamer.IsConnected;
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

    private void SetRecordingContext(ChatId chatId, string sessionToken, ChatEntryId? repliedChatEntryId)
    {
        lock (_sync)
        {
            _chatId = chatId;
            _sessionToken = sessionToken;
            _repliedChatEntryId = repliedChatEntryId;
        }
    }

    private void ClearRecordingContext()
    {
        lock (_sync)
        {
            _chatId = null;
            _sessionToken = null;
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

    private ValueTask OnAudioPowerChange(double power)
        => RecorderStateHub.OnAudioPowerChange(power);

    private async ValueTask MicrophoneIsCaptured(double gain)
    {
        await SetSignalDetected(true).ConfigureAwait(false);
        await RecorderStateHub.MicrophoneIsCaptured(gain).ConfigureAwait(false);
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
    private async Task ProcessAudioStream(
        IAsyncEnumerable<IMemoryOwner<float>> frames,
        CancellationToken cancellationToken)
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

    private async Task<ChannelWriter<IMemoryOwner<byte>>?> CreateAudioStream(CancellationToken token)
    {
        string? sessionToken;
        ChatId? chatId;
        ChatEntryId? repliedChatEntryId;

        lock (_sync) {
            sessionToken = _sessionToken;
            chatId = _chatId;
            repliedChatEntryId = _repliedChatEntryId;
        }

        if (sessionToken is null || chatId is null)
            return null;

        await _streamer.EnsureConnected(true, token).ConfigureAwait(false);
        await SetConnected(_streamer.IsConnected).ConfigureAwait(false);

        var stream = Channel.CreateBounded<IMemoryOwner<byte>>(
            new BoundedChannelOptions(Constants.Audio.StreamingChannelCapacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });

        // TODO(AK): Specify PreSkip
        _sendTask = _streamer.Send(sessionToken, chatId, repliedChatEntryId, 0, stream.Reader.ReadAllAsync(token).SuppressCancellation(token), token);
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
        while (stream.Reader.TryRead(out var frame))
            frame.Dispose();
    }

    // Separate class to handle the complex audio processing logic
    private sealed class AudioStreamProcessor(MauiRecorderEngine engine, ILogger log)
    {
        private readonly VoiceActivityDetector _vad = engine.VoiceActivityDetector;
        private readonly IAudioCodec _audioCodec = engine.AudioCodec;

        private readonly BlockRingBuffer<float> _vadBuffer = new (Constants.Audio.RecordingSampleRate * 10); // 10s
        private readonly BlockRingBuffer<float> _encodingBuffer = new (Constants.Audio.RecordingSampleRate * 2); // 2s

        private CancellationTokenSource? _encodeSendCts;
        private Task? _encodeSendTask;
        private bool _voiceActive;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public async Task Process(
            IAsyncEnumerable<IMemoryOwner<float>> frames,
            CancellationToken cancellationToken)
        {
            await _vad.EnsureInitialized(cancellationToken).ConfigureAwait(false);
            _vad.Reset();

            var lastMicCapturedAt = TimeSpan.Zero;
            var minInterval = TimeSpan.FromMilliseconds(200);

            try {
                await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    using var _ = frame;
                    var memory = frame.Memory;

                    // Process the audio frame
                    await ProcessAudioFrame(memory, cancellationToken).ConfigureAwait(false);

                    // Throttle microphone capture notifications
                    var now = _stopwatch.Elapsed;
                    if (now - lastMicCapturedAt >= minInterval) {
                        lastMicCapturedAt = now;
                        var gain = AudioExt.ApproximateGain(memory.Span);
                        await engine.MicrophoneIsCaptured(gain).ConfigureAwait(false);
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

        private async Task ProcessAudioFrame(ReadOnlyMemory<float> frame, CancellationToken cancellationToken)
        {
            // Push frame to buffers
            var isVadPushed = _vadBuffer.TryPush(frame.Span);
            bool isEncodingPushed;

            if (_voiceActive)
                isEncodingPushed = _encodingBuffer.TryPush(frame.Span);
            else {
                isEncodingPushed = _encodingBuffer.TryPush(frame.Span);
                if (!isEncodingPushed) {
                    // Trim oldest audio to maintain preroll
                    var retryCount = 0;
                    while (!isEncodingPushed && retryCount++ < 32) {
                        if (_encodingBuffer.TryPull(Constants.Audio.OpusFrameLength, out var dropped))
                            using (dropped) {
                                /* drop */
                            }
                        else
                            break;

                        isEncodingPushed = _encodingBuffer.TryPush(frame.Span);
                    }
                }
            }

            if (!isVadPushed || (_voiceActive && !isEncodingPushed))
                await Task.WhenAll(_vadBuffer.WhenPulled, _encodingBuffer.WhenPulled)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

            await engine.SetSignalDetected(true).ConfigureAwait(false);
        }

        private async Task ProcessVadEvents(CancellationToken cancellationToken)
        {
            while (_vadBuffer.TryPull(Constants.Audio.VadFrameLength, out var vadFrame)) {
                using var _ = vadFrame;
                var vadResult = _vad.AppendChunk(vadFrame.Memory.Span);

                if (vadResult.HasEvent)
                    await HandleVadEvent(vadResult.Change!.Value, cancellationToken).ConfigureAwait(false);
                else if (_vad.LastActivityEvent.Kind == VoiceActivityKind.Start)
                    // Maintain UI/JS heartbeat
                    await engine.OnAudioPowerChange(vadResult.Gain).ConfigureAwait(false);
            }
        }

        private async Task HandleVadEvent(VoiceActivityChange change, CancellationToken cancellationToken)
        {
            if (change.Kind == VoiceActivityKind.Start) {
                if (_voiceActive) return;

                var stream = await engine.CreateAudioStream(cancellationToken).ConfigureAwait(false);
                if (stream == null) return;

                var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var localTask = BackgroundTask.Run(
                    () => EncodeAndSend(stream, localCts.Token),
                    localCts.Token);

                _encodeSendCts = localCts;
                _encodeSendTask = localTask;
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

        private async Task StopEncodeSendWorker()
        {
            var localCts = Interlocked.Exchange(ref _encodeSendCts, null);
            if (localCts != null) {
                try { await localCts.CancelAsync().ConfigureAwait(false); }
                catch {
                    /* ignore */
                }
                localCts.Dispose();
            }

            var localTask = Interlocked.Exchange(ref _encodeSendTask, null);
            if (localTask != null)
                try { await localTask.ConfigureAwait(false); }
                catch (Exception e) {
                    log.LogError(e, "Failed to stop encode/send worker");
                }
        }

        private async Task EncodeAndSend(ChannelWriter<IMemoryOwner<byte>> stream, CancellationToken cancellationToken)
        {
            try {
                await foreach (var packet in _audioCodec.Encode(ReadFrames(), cancellationToken).ConfigureAwait(false)) {
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

        private IAsyncEnumerable<IMemoryOwner<float>> ReadFrames()
            => _encodeSendCts?.Token is { } cancellationToken
                ? _encodingBuffer.PullAll(Constants.Audio.OpusFrameLength, cancellationToken)
                : AsyncEnumerable.Empty<IMemoryOwner<float>>();
    }
    #endregion
}

