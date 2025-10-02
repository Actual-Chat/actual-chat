using System.Buffers;
using ActualChat.App.Maui.Playback;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Recording;

public class IosAudioCapture(AppUIHub hub) : IAudioCapture
{
    [field: AllowNull, MaybeNull]
    public ResamplerFactory ResamplerFactory => field ??= hub.Services.GetRequiredService<ResamplerFactory>();
    private static readonly int MaxQueueLength = (int)(TimeSpan.FromSeconds(5) / Constants.Audio.OpusFrameDuration);

    [field: AllowNull, MaybeNull]
    private AudioEngine Engine => field ??= hub.Services.GetRequiredService<AudioEngine>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.Services.LogFor(GetType());

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
        => Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(CaptureInternal(cancellationToken));

    private async IAsyncEnumerable<IMemoryOwner<float>> CaptureInternal([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<IMemoryOwner<float>>(new BoundedChannelOptions(MaxQueueLength) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true,
        });
        using var engine = new AVAudioEngine();
        var hwFormat = engine.InputNode.GetBusOutputFormat(0);
        using var resampler = ResamplerFactory.Create(hwFormat, AudioEngine.VoiceRecordingFormat);
        var frameLength = (int)(hwFormat.SampleRate / 1000 * Constants.Audio.OpusFrameDurationMs);
        engine.InputNode.InstallTapOnBus(0, (uint)frameLength, hwFormat, HandleSamples);
        engine.StartAndReturnError(out var error);
        error.Assert();

        try {
            await foreach (var memoryOwner in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return memoryOwner;
        }
        finally {
            engine.InputNode.RemoveTapOnBus(0);
            engine.Stop();
            channel.Writer.TryComplete();
            DisposeRemaining();
        }

        yield break;

        void HandleSamples(AVAudioPcmBuffer buffer, AVAudioTime when)
        {
            // TODO: use ring buffer to offload resampling to the other thread
            try {
                var resampled = resampler.Transform(buffer);
                var resampledData = resampled.ToFloats();
                channel.Writer.TryWrite(new FloatArrayMemoryOwner(resampledData));
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to resample buffer");
            }
        }

        void DisposeRemaining()
        {
            while (channel.Reader.TryRead(out var frame))
                frame.DisposeSilently();
        }
    }
}
