using System.Buffers;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class AppleAudioCapture(AppUIHub hub) : IAudioCapture
{
    private static readonly TimeSpan InputNodeHoldTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InputNodeHeartbeatTimeout = TimeSpan.FromSeconds(30);

    private static long _inputNodeHeldAt;
    private static long _inputNodeHeartbeatAt;

    public static TimeSpan? InputNodeHeldFor {
        get {
            var heldAt = Volatile.Read(ref _inputNodeHeldAt);
            return heldAt == 0 ? null : new CpuTimestamp(heldAt).Elapsed;
        }
    }

    public static bool IsInputNodeHeld {
        get {
            // The hold itself is the signal - an engine that's running but not delivering (a
            // decoder hang, or a media-services reset) still owns the node. The heartbeat only
            // extends the hold past InputNodeHoldTimeout, which exists to heal a leaked latch.
            if (InputNodeHeldFor is not { } heldFor)
                return false;
            if (heldFor < InputNodeHoldTimeout)
                return true;

            var heartbeatAt = Volatile.Read(ref _inputNodeHeartbeatAt);
            return new CpuTimestamp(heartbeatAt).Elapsed < InputNodeHeartbeatTimeout;
        }
    }

    public ResamplerFactory ResamplerFactory => field ??= hub.Services.GetRequiredService<ResamplerFactory>();

    private AudioEngines AudioEngines => field ??= hub.Services.GetRequiredService<AudioEngines>();
    private AudioSession AudioSession => field ??= hub.Services.GetRequiredService<AudioSession>();
    private ILogger Log => field ??= hub.Services.LogFor(GetType());

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
        => Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(CaptureInternal(cancellationToken));

    private async IAsyncEnumerable<IMemoryOwner<float>> CaptureInternal([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // First statement, before TryTake() stops the pre-roll engine: everything below is
        // blocking native work, and a press landing in that window would see a free input node
        // and publish a second AVAudioEngine onto it - see PttPreRoll.Start().
        var heldAt = CpuTimestamp.Now.Value;
        Volatile.Write(ref _inputNodeHeartbeatAt, heldAt);
        Volatile.Write(ref _inputNodeHeldAt, heldAt);
        // Resolved out here so the finally can stop it without reaching into DI, which may
        // already be gone if this enumerator is disposed during shutdown.
        var engine = AudioEngines.Recording;
        try {
            // TryTake() stops its own AVAudioEngine, and that must happen before the engine
            // above is used below - two AVAudioEngine instances must never hold the hardware
            // input node at once.
            var preRoll = PttPreRoll.TryTake();
            Log.LogInformation("CaptureInternal: starting");
            using var outBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
            // TODO(FC): restore AEC/NS/AGC on Mac Catalyst.
            // Voice processing breaks on Mac Catalyst: the engine's recording graph has no
            // active output side, so the VoiceProcessor's downlink DSP can't get valid sample
            // timestamps and either errors out continuously or delivers a single initial
            // buffer then goes silent. Wiring a silent AVAudioPlayerNode to MainMixerNode
            // suppressed the error spam but didn't restore steady-state frame delivery.
            // Until we find a stable workaround, ship without VP on Mac Catalyst (desktops
            // are typically used with headphones, so echo is a minor regression vs iOS).
            //
            // This must precede GetOutputFormat(): enabling VP can change the input node's
            // output format, and both the resampler and the tap below are built from it. Reading
            // it first would leave Transform() throwing on every buffer - silent dead recording.
            if (!OperatingSystem.IsMacCatalyst())
                engine.Input.SetVoiceProcessingEnabled(true);

            var hwFormat = engine.Input.GetOutputFormat();
            using var resampler = ResamplerFactory.Create(hwFormat, AudioEngine.VoiceRecordingFormat);
            if (preRoll is { } take) {
                // Only a format match is safe: a route change between arming and draining would make
                // the buffered samples the wrong rate for this resampler.
                if (take.Format.SampleRate.Equals(hwFormat.SampleRate)
                    && take.Format.ChannelCount == hwFormat.ChannelCount) {
                    // Resampler.Transform runs a single AVAudioConverter.ConvertToBuffer call into an
                    // intermediate buffer sized at hwFormat.SampleRate frames - anything past that in
                    // one call is silently dropped. Chunk to one input-second per call (comfortably
                    // under that bound at realistic mic rates) so an 8 s pre-roll can't be truncated
                    // to ~3 s. Do not "simplify" this back into a single Transform call.
                    var chunkFrameCount = (int)hwFormat.SampleRate;
                    var drainedSampleCount = 0;
                    var isTruncated = false;
                    for (var offset = 0; offset < take.Samples.Length && !isTruncated; offset += chunkFrameCount) {
                        var chunkLength = Math.Min(chunkFrameCount, take.Samples.Length - offset);
                        using var preRollBuffer = new AVAudioPcmBuffer(hwFormat, (uint)chunkLength);
                        preRollBuffer.SetData(take.Samples.AsSpan(offset, chunkLength));
                        resampler.Transform(preRollBuffer, outBuffer);
                        drainedSampleCount += chunkLength;
                        // Transform() doesn't surface BlockRingBuffer.TryWrite's result, so fullness
                        // is the only signal available here that a write came up short.
                        if (outBuffer.IsFull) {
                            isTruncated = true;
                            Log.LogWarning("Pre-roll drain: outBuffer is full, the rest of the pre-roll was dropped");
                        }
                    }
                    Log.LogInformation("Drained {Count} pre-roll samples", drainedSampleCount);
                }
                else
                    Log.LogWarning("Dropped the pre-roll: format changed since it was captured");
            }
            using var _2 = engine.Input.Tap(HandleSamples);
            engine.EnsureRunning();
            // Voice processing activation can route audio to the earpiece — fix it
            await AudioSession.EnsureCorrectOutputRoute().ConfigureAwait(false);

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
            yield break;

            [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
            void HandleSamples(AVAudioPcmBuffer pcmBuffer, AVAudioTime when)
            {
                // The engine is alive and still owns the input node - see IsInputNodeHeld.
                Volatile.Write(ref _inputNodeHeartbeatAt, CpuTimestamp.Now.Value);
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
        finally {
            // Ends the engine and its VPIO with the capture rather than with the focus scope,
            // which in walkie-talkie mode is released minutes later. It also has to happen
            // before the latch reports the input node free, or a PTT press landing in between
            // would start a second AVAudioEngine on a node this one still holds.
            engine.Release();
            Volatile.Write(ref _inputNodeHeldAt, 0);
            Log.LogInformation("CaptureInternal: stopped");
        }
    }
}
