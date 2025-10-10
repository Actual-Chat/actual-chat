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
        Log.LogInformation("CaptureInternal: starting");
        using var engineLease = await AudioEngines.Rent(AudioMode.Recording).ConfigureAwait(false);
        var engine = engineLease.Resource;
        using var outBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        var hwFormat = engine.Input.GetOutputFormat();
        using var resampler = ResamplerFactory.Create(hwFormat, AudioEngine.VoiceRecordingFormat);
        engine.Input.SetVoiceProcessingEnabled(true);
        using var _2 = engine.Input.Tap(HandleSamples);
        engine.EnsureRunning();

        try {
            await foreach (var memoryOwner in outBuffer.PullAll(Constants.Audio.OpusFrameLength, cancellationToken)
                               .ConfigureAwait(false))
                yield return memoryOwner;
        }
        finally {
            Log.LogInformation("CaptureInternal: stopped");
        }
        yield break;

        void HandleSamples(AVAudioPcmBuffer pcmBuffer, AVAudioTime when)
        {
            try {
                var frameLength = pcmBuffer.FrameLength / hwFormat.SampleRate * AudioEngine.VoiceRecordingFormat.SampleRate;
                if (outBuffer.RemainingCapacity < frameLength) {
                    // Log.LogWarning("!!! Buffer full, dropping samples");
                    return;
                }

                var resampled = resampler.Transform(pcmBuffer);
                outBuffer.TryPush(resampled.AsReadOnlySpan());
            }
            catch (Exception e) {
                Log.LogError(e, "!!! Failed to handle recorded samples");
            }
        }
    }
}
