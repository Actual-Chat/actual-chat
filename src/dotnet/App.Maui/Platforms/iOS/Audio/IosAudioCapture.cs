using System.Buffers;
using ActualChat.App.Maui.Audio;
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
    private AudioEngines AudioEngines => field ??= hub.Services.GetRequiredService<AudioEngines>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.Services.LogFor(GetType());

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
        => Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(CaptureInternal(cancellationToken));

    private async IAsyncEnumerable<IMemoryOwner<float>> CaptureInternal([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var engineLease = await AudioEngines.Rent(AudioMode.Recording).ConfigureAwait(false);
        var engine = engineLease.Resource;
        var channel = Channel.CreateBounded<IMemoryOwner<float>>(new BoundedChannelOptions(MaxQueueLength) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true,
        });
        var hwFormat = engine.Input.GetOutputFormat();
        using var resampler = ResamplerFactory.Create(hwFormat, AudioEngine.VoiceRecordingFormat);
        engine.Input.SetVoiceProcessingEnabled(true);
        using var _2 = engine.Input.Tap(HandleSamples);
        engine.EnsureRunning();

        try {
            await foreach (var memoryOwner in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return memoryOwner;
        }
        finally {
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
