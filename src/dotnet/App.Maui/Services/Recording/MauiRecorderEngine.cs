using System.Buffers;
using ActualChat.Audio;
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

    private MicrophonePermissionHandler MicrophonePermissionHandler => field ??= _hub.Services.GetRequiredService<MicrophonePermissionHandler>();
    private IAudioCapture AudioCapture => field ??= _hub.Services.GetRequiredService<IAudioCapture>();
    private VoiceActivityDetector VoiceActivityDetector => field ??= _hub.Services.GetRequiredService<VoiceActivityDetector>();
    private IAudioRecorderBackend AudioRecorderBackend => field ??= _hub.Services.GetRequiredService<IAudioRecorderBackend>();
    private IAudioCodec AudioCodec => field ??= _hub.Services.GetRequiredService<IAudioCodec>();
    private ILogger Log => field ??= _hub.LogFor<MauiRecorderEngine>();
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

    private async Task<ChannelWriter<IMemoryOwner<byte>>?> CreateAudioStream(CancellationToken cancellationToken)
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

        await _streamer.EnsureConnected(true, cancellationToken).ConfigureAwait(false);
        await SetConnected(_streamer.IsConnected).ConfigureAwait(false);

        var stream = Channel.CreateBounded<IMemoryOwner<byte>>(
            new BoundedChannelOptions(Constants.Audio.StreamingChannelCapacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });

        // TODO(AK): Specify PreSkip
        _sendTask = _streamer.Send(sessionToken,
            chatId,
            repliedChatEntryId,
            0,
            stream.Reader.ReadAllAsync(cancellationToken),
            cancellationToken);
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

        private readonly BlockRingBuffer<float> _vadBuffer = new (Constants.Audio.RecordingSampleRate * 2); // 2s
        private readonly BlockRingBuffer<float> _encodingBuffer = new (Constants.Audio.RecordingSampleRate * 2); // 2s

        private FuncWorker? _encodeSendWorker;
        private bool _voiceActive;

        public async Task Process(
            IAsyncEnumerable<IMemoryOwner<float>> frames,
            CancellationToken cancellationToken)
        {
            const int samplesPerRecordingInProgressCall = Constants.Audio.RecordingSampleRate / 1000 * 200; // 200ms
            using var gainBuffer = new BlockRingBuffer<float>(Constants.Audio.OpusFrameLength * 10); // 200ms
            await _vad.EnsureInitialized(cancellationToken).ConfigureAwait(false);
            _vad.Reset();
            var samplesSinceLastReport = samplesPerRecordingInProgressCall;
            try {
                await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    using var _ = frame;
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
                    gainBuffer.TryPush(memory.Span);
                    if (gainBuffer.TryPull(Constants.Audio.VadFrameLength * 3, out var gainMemory)) {
                        using var __ = gainMemory;
                        var gain = AudioExt.ApproximateGain(gainMemory.Memory.Span);
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

        private async Task ProcessAudioFrame(ReadOnlyMemory<float> frame, CancellationToken cancellationToken)
        {
            // Push frame to buffers
            var isVadPushed = _vadBuffer.TryPush(frame.Span);
            bool isEncodingPushed;
            var isVoiceActive = _voiceActive;

            if (isVoiceActive)
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

            // log.LogInformation("ProcessAudioFrame: {Vad}, Encoding: {Encoding}, VoiceActive: {VoiceActive}", isVadPushed, isEncodingPushed, isVoiceActive);
            if (!isVadPushed || (isVoiceActive && !isEncodingPushed))
                await Task.WhenAll(_vadBuffer.WhenPulled, _encodingBuffer.WhenPulled)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
        }

        private async Task ProcessVadEvents(CancellationToken cancellationToken)
        {
            // Process VAD events in batches to reduce CPU usage
            while (_vadBuffer.TryPull(Constants.Audio.VadFrameLength * 5, out var vadFrame)) {
                using var _ = vadFrame;
                var vadResults = _vad.AppendChunk(vadFrame.Memory.Span);

                foreach (var vadResult in vadResults)
                    if (vadResult.HasEvent)
                        await HandleVadEvent(vadResult.Change!.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task HandleVadEvent(VoiceActivityChange change, CancellationToken cancellationToken)
        {
            if (change.Kind == VoiceActivityKind.Start) {
                if (_voiceActive) return;

                var stream = await engine.CreateAudioStream(cancellationToken).ConfigureAwait(false);
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

        private Task StopEncodeSendWorker()
            => Interlocked.Exchange(ref _encodeSendWorker, null) is { } worker
                ? BackgroundTask.Run(worker.Stop, log, "Failed to stop encode/send worker")
                : Task.CompletedTask;

        private async Task EncodeAndSend(ChannelWriter<IMemoryOwner<byte>> stream, CancellationToken cancellationToken)
        {
            try {
                var frames = _encodingBuffer.PullAll(Constants.Audio.OpusFrameLength, cancellationToken);
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
