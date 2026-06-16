using System.Buffers;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class AppleAudioCapture(AppUIHub hub) : IAudioCapture
{
    public ResamplerFactory ResamplerFactory => field ??= hub.Services.GetRequiredService<ResamplerFactory>();

    private AudioEngines AudioEngines => field ??= hub.Services.GetRequiredService<AudioEngines>();
    private AudioSession AudioSession => field ??= hub.Services.GetRequiredService<AudioSession>();
    private ILogger Log => field ??= hub.Services.LogFor(GetType());

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
        => Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(CaptureInternal(cancellationToken));

    private async IAsyncEnumerable<IMemoryOwner<float>> CaptureInternal([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogInformation("CaptureInternal: starting");
        var engine = AudioEngines.Recording;
        using var outBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        var hwFormat = engine.Input.GetOutputFormat();
        using var resampler = ResamplerFactory.Create(hwFormat, AudioEngine.VoiceRecordingFormat);
        // TODO(FC): restore AEC/NS/AGC on Mac Catalyst.
        // Voice processing breaks on Mac Catalyst: the engine's recording graph has no
        // active output side, so the VoiceProcessor's downlink DSP can't get valid sample
        // timestamps and either errors out continuously or delivers a single initial
        // buffer then goes silent. Wiring a silent AVAudioPlayerNode to MainMixerNode
        // suppressed the error spam but didn't restore steady-state frame delivery.
        // Until we find a stable workaround, ship without VP on Mac Catalyst (desktops
        // are typically used with headphones, so echo is a minor regression vs iOS).
        if (!OperatingSystem.IsMacCatalyst())
            engine.Input.SetVoiceProcessingEnabled(true);
        using var _2 = engine.Input.Tap(HandleSamples);
        engine.EnsureRunning();
        // Voice processing activation can route audio to the earpiece — fix it
        await AudioSession.EnsureCorrectOutputRoute().ConfigureAwait(false);

        try {
            var frameLen = Constants.Audio.OpusFrameLength;
            while (!cancellationToken.IsCancellationRequested) {
                var owner = ArrayPools.SharedFloatPool.LeaseArrayOwner(frameLen, true);
                if (!outBuffer.TryRead(owner.Span, out var whenReady)) {
                    owner.Dispose();
                    await whenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                yield return owner;
            }
        }
        finally {
            Log.LogInformation("CaptureInternal: stopped");
        }
        yield break;

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        void HandleSamples(AVAudioPcmBuffer pcmBuffer, AVAudioTime when)
        {
            try {
                var estimatedResampledLength = pcmBuffer.FrameLength / hwFormat.SampleRate * AudioEngine.VoiceRecordingFormat.SampleRate;
                if (outBuffer.RemainingCapacity < estimatedResampledLength) {
                    Log.LogWarning("Buffer full, dropping samples");
                    return;
                }

                resampler.Transform(pcmBuffer, outBuffer);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to handle recorded samples");
            }
        }
    }
}
