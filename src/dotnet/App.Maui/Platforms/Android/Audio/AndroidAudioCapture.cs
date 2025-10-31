using System.Buffers;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Audio;
using Android.Media;
using Encoding = Android.Media.Encoding;

namespace ActualChat.App.Maui.Audio;

public class AndroidAudioCapture(ILogger<AndroidAudioCapture> log) : IAudioCapture
{
    private const int NativeSampleRate = 48000;
    private const int FrameSamples = 960; // 20 ms at 48 kHz
    private const int BytesPerSample = sizeof(float);

    public ILogger<AndroidAudioCapture> Log { get; } = log;

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
    {
        // Configure AudioRecord for float32 PCM mono at native 48kHz (API 23+ supports PCM_FLOAT)
        // We'll resample from 48kHz to 16kHz using Resampler

        var minBufferBytes = AudioRecord.GetMinBufferSize(NativeSampleRate, ChannelIn.Mono, Android.Media.Encoding.PcmFloat);
        if (minBufferBytes <= 0)
            return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);

        // At 48kHz, 20ms = 960 samples (will be resampled to 320 samples at 16kHz)
        var bufferBytes = Math.Max(minBufferBytes, FrameSamples * BytesPerSample * 4); // some headroom

        AudioRecord? recorder = null;
        try {
            recorder = new AudioRecord(
                /* audioSource: */ Android.Media.AudioSource.VoiceCommunication,
                /* sampleRateInHz: */ NativeSampleRate,
                /* channelConfig: */ ChannelIn.Mono,
                /* audioFormat: */ Android.Media.Encoding.PcmFloat,
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
        var resampler = new Resampler();

        _ = BackgroundTask.Run(Producer, cancellationToken);

        // Return enumerator
        return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(Enumerate(cancellationToken));

        async Task Producer()
        {
            const int readBy = FrameSamples * 2;
            var floatReadBuffer = ArrayBuffer<float>.Lease(false, readBy * 2);
            var resampleOutputBuffer = ArrayBuffer<float>.Lease(false, resampler.GetMaxOutputLength(readBy));
            try {
                recorder!.StartRecording();

                while (!cancellationToken.IsCancellationRequested) {
                    // int readCount;
                    // try {
                        // Use async read of float samples; cancel via WaitAsync
                        // readMode: 0 = blocking, 1 = non-blocking (constants per Android API)
                        // readCount = await recorder
                        //     .ReadAsync(floatReadBuffer.Buffer, 0, readBy, 0) // 40 ms at 48 kHz
                        //     .WaitAsync(cancellationToken)
                        //     .ConfigureAwait(false);

                    // }
                    // catch (OperationCanceledException) {
                    //     break;
                    // }

                    var readCount = recorder.Read(floatReadBuffer.Buffer, 0, readBy, 1);
                    if (readCount <= 0) {
                        if (readCount < 0)
                            break; // error

                        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    // Resample from 48kHz to 16kHz
                    var outputCount = resampler.ProcessChunk(
                        floatReadBuffer.Buffer.AsSpan(0, readCount),
                        resampleOutputBuffer.Buffer.AsSpan());

                    // Push resampled audio to ring buffer; backpressure if full
                    while (!ringBuffer.TryPush(resampleOutputBuffer.Buffer.AsSpan(0, outputCount)))
                        await ringBuffer.WhenPulled.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error while capturing audio on Android");
            }
            finally {
                floatReadBuffer.Release();
                resampleOutputBuffer.Release();
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
