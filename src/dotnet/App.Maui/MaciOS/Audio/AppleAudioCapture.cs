using System.Buffers;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.UI.Blazor.App.Services;
using AudioToolbox;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public sealed class AppleAudioCapture(AppUIHub hub) : IAudioCapture
{
    public ResamplerFactory ResamplerFactory => field ??= hub.Services.GetRequiredService<ResamplerFactory>();

    private AudioEngines AudioEngines => field ??= hub.Services.GetRequiredService<AudioEngines>();
    private AudioSession AudioSession => field ??= hub.Services.GetRequiredService<AudioSession>();
    private ILogger Log => field ??= hub.Services.LogFor(GetType());

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
        => Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(CaptureInternal(cancellationToken));

    private async IAsyncEnumerable<IMemoryOwner<float>> CaptureInternal(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogInformation("CaptureInternal: starting");
        var engine = AudioEngines.Recording;
        using var outBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        // Enabling VP changes the input node's format, so it must precede GetOutputFormat
        engine.EnableVoiceProcessing();
        var hwFormat = engine.Input.GetOutputFormat();
        using var resampler = ResamplerFactory.Create(hwFormat, AudioEngine.VoiceRecordingFormat);
        using var sinkBuffer = OperatingSystem.IsMacCatalyst()
            ? new AVAudioPcmBuffer(hwFormat, (uint)hwFormat.SampleRate)
            : null;
        using var _2 = OperatingSystem.IsMacCatalyst()
            ? engine.AttachVoiceProcessedInputSink(HandleSinkSamples)
            : engine.Input.Tap(HandleSamples);
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
        int HandleSinkSamples(AudioTimeStamp timestamp, uint frameCount, AudioBuffers inputData) {
            try {
                if (frameCount == 0 || frameCount > sinkBuffer!.FrameCapacity)
                    return 0;

                unsafe {
                    var source = new ReadOnlySpan<float>((void*)inputData[0].Data, (int)frameCount);
                    var target = new Span<float>((void*)((float**)sinkBuffer.FloatChannelData)[0], (int)frameCount);
                    source.CopyTo(target);
                }
                sinkBuffer.FrameLength = frameCount;
                HandleSamples(sinkBuffer, null!);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to handle sink samples");
            }
            return 0;
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        void HandleSamples(AVAudioPcmBuffer pcmBuffer, AVAudioTime when) {
            try {
                var estimatedResampledLength =
                    pcmBuffer.FrameLength / hwFormat.SampleRate * AudioEngine.VoiceRecordingFormat.SampleRate;
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
