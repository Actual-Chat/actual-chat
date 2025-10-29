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
                /* audioSource: */ Android.Media.AudioSource.VoiceRecognition,
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
                 /* ignore */
            }
            return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);
        }

        var ringBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);

        _ = BackgroundTask.Run(Producer, cancellationToken);

        // Return enumerator
        return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(Enumerate(cancellationToken));

        async Task Producer()
        {
            var floatReadBuffer = ArrayBuffer<float>.Lease(false, frameSamples * 4);
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

                    // Push to ring buffer; backpressure if full
                    while (!ringBuffer.TryPush(floatReadBuffer.Buffer.AsSpan(0, readCount)))
                        await ringBuffer.WhenPulled.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                catch { /* ignore */ }

                try {
                    recorder.Release();
                }
                catch {
                     /* ignore */
                }
            }
        }

        async IAsyncEnumerable<IMemoryOwner<float>> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            try {
                while (!ct.IsCancellationRequested) {
                    if (!ringBuffer.TryPull(Constants.Audio.OpusFrameLength, out var block)) {
                        await ringBuffer.WhenPushed.WaitAsync(ct).ConfigureAwait(false);
                        continue;
                    }
                    yield return block;
                }
            }
            finally {
                ringBuffer.DisposeSilently();
            }
        }
    }
}
