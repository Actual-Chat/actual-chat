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
        // 250ms of kernel-side ring. Note: this protects persisted-blob
        // completeness (samples survive reader-thread stalls instead of being
        // dropped on kernel overrun) — it does NOT remove the live-gap heard
        // by listeners, since we still send no packets while the thread is
        // stalled. Live-gap fix needs to prevent the stall itself (Process on
        // dedicated thread, etc.). 250ms covers most observed scheduling
        // hiccups (~80–130ms) and leaves headroom; ~16KB memory.
        var bufferBytes = Math.Max(minBufferBytes, sampleRate * bytesPerSample / 4);

        AudioRecord? recorder = null;
        try {
            recorder = new AudioRecord(
                /* audioSource: */ AudioSource.VoiceCommunication,
                /* sampleRateInHz: */ sampleRate,
                /* channelConfig: */ channelConfig,
                /* audioFormat: */ encoding,
                /* bufferSizeInBytes: */ bufferBytes);

            if (recorder.State != Android.Media.State.Initialized) {
                // Usually the OS silently withholding the mic rather than a bad configuration: a
                // foreground service started from the background gets no while-in-use permissions,
                // which is exactly how a wake-driven walkie reply reaches this point.
                Log.LogWarning("AudioRecord didn't initialize (state = {State}) - is the mic available?",
                    recorder.State);
                recorder.Release();
                return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);
            }

            var actualFrames = recorder.BufferSizeInFrames;
            Log.LogInformation(
                "AudioRecord initialized: requested {RequestedBytes}B, actual {ActualFrames} frames ({ActualMs}ms at {SampleRate}Hz)",
                bufferBytes, actualFrames, actualFrames * 1000 / sampleRate, sampleRate);
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
        // Dedicated high-priority thread instead of ThreadPool. Two reasons:
        //   1) AudioRecord.Read() is a blocking JNI call; running it on a ThreadPool
        //      worker means each read holds a worker for ~40ms and resumption after
        //      it depends on the pool not being saturated by WebView/video tasks.
        //   2) THREAD_PRIORITY_URGENT_AUDIO is the standard Android scheduler hint
        //      for AudioRecord loops — keeps us above the WebView's normal threads
        //      so the OS scheduler doesn't deschedule us under contention. Without
        //      this, capture-cadence stalls of 400+ms cause kernel-side AudioRecord
        //      buffer overflow → silent sample loss before our pipeline sees it.
        var captureThread = new Thread(Producer) {
            Name = "audio-capture",
            IsBackground = true,
        };
        captureThread.Start();
        // Return enumerator
        return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(Enumerate(cancellationToken));

        void Producer()
        {
            Android.OS.Process.SetThreadPriority(Android.OS.ThreadPriority.UrgentAudio);

            var floatReadBuffer = ArrayBuffer<float>.Lease(false, frameSamples * 4);
            // Cadence tracking: each Read requests `frameSamples * 2` floats
            // (= 40ms at 16kHz mono). Anything >80ms means at least one expected
            // window was missed. GC counts surface GC-pause culprits across stages.
            var lastReadStamp = 0L;
            var lastReadLogStamp = Stopwatch.GetTimestamp();
            var readGapsInWindow = 0;
            var readMaxGapMs = 0.0;
            var readGen0Start = GC.CollectionCount(0);
            var readGen1Start = GC.CollectionCount(1);
            var readGen2Start = GC.CollectionCount(2);

            // Make the blocking Read() return when cancellation fires: Stop() is
            // safe to call from a different thread; the active read returns with
            // whatever was already in the kernel buffer.
            using var stopOnCancel = cancellationToken.Register(() => {
                try {
                    if (recorder!.RecordingState == RecordState.Recording)
                        recorder.Stop();
                }
                catch { /* Ignore */ }
            });

            try {
                recorder!.StartRecording();

                while (!cancellationToken.IsCancellationRequested) {
                    int readCount;
                    try {
                        // readMode = 0 == AudioRecord.READ_BLOCKING (per Android API).
                        // ReadAsync overload was used previously with the same int sentinel.
                        readCount = recorder.Read(
                            floatReadBuffer.Buffer, 0, frameSamples * 2, 0);
                    }
                    catch (Exception) {
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
