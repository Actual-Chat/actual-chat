using System.Buffers;
using ActualChat.App.Maui.Services.Recording;
using Android.Media;

namespace ActualChat.App.Maui.Recording;

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
        var frameSamples = Constants.Audio.VadFrameLength; // 32 ms at 16 kHz = 512 samples
        var bytesPerSample = sizeof(float);
        var bufferBytes = Math.Max(minBufferBytes, frameSamples * bytesPerSample * 4); // some headroom

        AudioRecord? recorder = null;
        try {
            recorder = new AudioRecord(
                /* audioSource: */ Android.Media.AudioSource.VoiceCommunication,
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

        var channel = Channel.CreateUnbounded<IMemoryOwner<float>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        // Producer task
        var produceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var produceToken = produceCts.Token;

        _ = BackgroundTask.Run(Producer, produceToken);

        // Return enumerator
        return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(Enumerate(produceToken));

        async Task Producer()
        {
            var floatReadBuffer = new float[Math.Max(Constants.Audio.VadFrameLength * 4, 4096)];

            try {
                recorder!.StartRecording();

                while (!produceToken.IsCancellationRequested) {
                    int readCount;
                    try {
                        // Use async read of float samples; cancel via WaitAsync
                        // readMode: 0 = blocking, 1 = non-blocking (constants per Android API)
                        var readTask = recorder.ReadAsync(floatReadBuffer, 0, floatReadBuffer.Length, 0);
                        readCount = await readTask.WaitAsync(produceToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        break;
                    }

                    if (readCount <= 0) {
                        if (readCount < 0)
                            break; // error

                        continue;
                    }

                    var owner = MemoryPool<float>.Shared.Rent(readCount);
                    try {
                        var dst = owner.Memory.Span[..readCount];
                        floatReadBuffer.AsSpan(0, readCount).CopyTo(dst);
                        if (channel.Writer.TryWrite(new BufferReference(owner.Memory[..readCount], owner)))
                            continue;

                        owner.Dispose();
                        break;
                    }
                    catch {
                        owner.Dispose();
                        throw;
                    }
                }
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error while capturing audio on Android");
            }
            finally {
                try {
                    if (recorder.RecordingState == RecordState.Recording)
                        recorder.Stop();
                }
                catch { /* ignore */ }

                try { recorder.Release(); } catch { /* ignore */ }
                channel.Writer.TryComplete();
            }
        }

        async IAsyncEnumerable<IMemoryOwner<float>> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            await using var ctr = ct.Register(() => {
                try { produceCts.Cancel(); } catch { /* ignore */ }
            });

            await foreach (var block in channel.Reader.ReadAllAsync(ct).SuppressCancellation(ct).ConfigureAwait(false))
                yield return block;
        }
    }

    private readonly struct BufferReference(Memory<float> memory, IMemoryOwner<float> owner): IMemoryOwner<float>
    {
        public Memory<float> Memory { get; } = memory;
        public void Dispose() => owner.Dispose();
    }
}
