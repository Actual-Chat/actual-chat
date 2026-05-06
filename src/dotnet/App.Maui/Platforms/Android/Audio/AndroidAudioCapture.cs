using System.Buffers;
using ActualChat.App.Maui.Services.Recording;
using Android.Media;

namespace ActualChat.App.Maui.Audio;

public class AndroidAudioCapture(ILogger<AndroidAudioCapture> log) : IAudioCapture
{
    public ILogger<AndroidAudioCapture> Log { get; } = log;

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
    {
        // Configure AudioRecord for float32 PCM mono at desired sample rate (API 23+ supports PCM_FLOAT)
        var sampleRate = Constants.Audio.RecordingSampleRate;
        var channelConfig = ChannelIn.Mono;
        var encoding = Android.Media.Encoding.PcmFloat;

        var minBufferBytes = AudioRecord.GetMinBufferSize(sampleRate, channelConfig, encoding);
        if (minBufferBytes <= 0)
            return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);

        // We'll read at least VAD frame size per push
        var frameSamples = Constants.Audio.OpusFrameLength; // 20 ms at 16 kHz = 320 samples
        var bytesPerSample = sizeof(float);
        var bufferBytes = Math.Max(minBufferBytes, frameSamples * bytesPerSample * 4); // some headroom

        AudioRecord? recorder = null;
        try {
            recorder = new AudioRecord(
                /* audioSource: */ AudioSource.VoiceCommunication,
                /* sampleRateInHz: */ sampleRate,
                /* channelConfig: */ channelConfig,
                /* audioFormat: */ encoding,
                /* bufferSizeInBytes: */ bufferBytes);

            if (recorder.State != Android.Media.State.Initialized) {
                recorder.Release();
                return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);
            }
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize AudioRecord");
            try {
                recorder?.Release();
            }
            catch {
                 /* Ignore */
            }
            return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);
        }

        var buffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        _ = BackgroundTask.Run(Producer, cancellationToken);
        // Return enumerator
        return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(Enumerate(cancellationToken));

        async Task Producer()
        {
            var floatReadBuffer = ArrayBuffer<float>.Lease(false, frameSamples * 4);
            // Cadence tracking: each ReadAsync requests `frameSamples * 2` floats
            // (= 40ms at 16kHz mono). Anything >80ms means at least one expected
            // window was missed — pinpoints whether AudioRecord itself stalls vs.
            // upstream (encode/send) stages. GC counts are process-wide; surfacing
            // them per stage lets cross-cadence correlations confirm GC-pause culprits.
            var lastReadStamp = 0L;
            var lastReadLogStamp = Stopwatch.GetTimestamp();
            var readGapsInWindow = 0;
            var readMaxGapMs = 0.0;
            var readGen0Start = GC.CollectionCount(0);
            var readGen1Start = GC.CollectionCount(1);
            var readGen2Start = GC.CollectionCount(2);
            try {
                recorder!.StartRecording();

                while (!cancellationToken.IsCancellationRequested) {
                    int readCount;
                    try {
                        // Use async read of float samples; cancel via WaitAsync
                        // readMode: 0 = blocking, 1 = non-blocking (constants per Android API)
                        readCount = await recorder
                            .ReadAsync(floatReadBuffer.Buffer, 0, frameSamples * 2, 0)
                            .WaitAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        break;
                    }

                    if (readCount <= 0) {
                        if (readCount < 0)
                            break; // error

                        continue;
                    }

                    // Push to ring buffer (fire-and-forget: drop if full)
                    buffer.TryWrite(floatReadBuffer.Buffer.AsSpan(0, readCount));

                    var nowStamp = Stopwatch.GetTimestamp();
                    if (lastReadStamp != 0) {
                        var deltaMs = Stopwatch.GetElapsedTime(lastReadStamp, nowStamp).TotalMilliseconds;
                        if (deltaMs > 80.0) {
                            readGapsInWindow++;
                            if (deltaMs > readMaxGapMs)
                                readMaxGapMs = deltaMs;
                        }
                    }
                    lastReadStamp = nowStamp;

                    if (Stopwatch.GetElapsedTime(lastReadLogStamp, nowStamp).TotalSeconds >= 1.0) {
                        var gen0 = GC.CollectionCount(0) - readGen0Start;
                        var gen1 = GC.CollectionCount(1) - readGen1Start;
                        var gen2 = GC.CollectionCount(2) - readGen2Start;
                        if (readGapsInWindow > 0)
                            Log.LogWarning(
                                "audio-capture-cadence: {Gaps} gap(s) >80ms in last second; max gap {MaxMs:F0}ms; gc 0/1/2={Gen0}/{Gen1}/{Gen2}",
                                readGapsInWindow, readMaxGapMs, gen0, gen1, gen2);
                        readGapsInWindow = 0;
                        readMaxGapMs = 0;
                        readGen0Start += gen0; readGen1Start += gen1; readGen2Start += gen2;
                        lastReadLogStamp = nowStamp;
                    }
                }
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error while capturing audio on Android");
            }
            finally {
                floatReadBuffer.Release();
                try {
                    if (recorder.RecordingState == RecordState.Recording)
                        recorder.Stop();
                }
                catch { /* Ignore */ }

                try {
                    recorder.Release();
                }
                catch {
                     /* Ignore */
                }
            }
        }

        async IAsyncEnumerable<IMemoryOwner<float>> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            try {
                const int frameSize = Constants.Audio.OpusFrameLength;
                while (!ct.IsCancellationRequested) {
                    var owner = ArrayPools.SharedFloatPool.LeaseArrayOwner(frameSize, true);
                    if (!buffer.TryRead(owner.Span, out var whenReady)) {
                        owner.Dispose();
                        await whenReady.WaitAsync(ct).ConfigureAwait(false);
                        continue;
                    }
                    yield return owner;
                }
            }
            finally {
                buffer.DisposeSilently();
            }
        }
    }
}
