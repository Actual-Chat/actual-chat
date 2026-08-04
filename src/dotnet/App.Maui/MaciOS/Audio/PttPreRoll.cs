using ActualChat.Audio;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Captures microphone audio from the moment Apple Push to Talk activates the audio session until
/// the app's own recorder exists, so a transmit from a killed process keeps its first words.
/// </summary>
public static class PttPreRoll
{
    private static readonly Lock Lock = new();
    private static long _lastToken;
    // The highest token a TryTake()/Discard() has already "closed" - even one that ran while
    // nothing was published yet, because Start() may still be mid-flight on another thread. A
    // token may publish only while it's still the newest AND still unclosed; see Start().
    private static long _closedToken;
    private static AVAudioEngine? _engine;
    private static AVAudioFormat? _format;
    private static PreRollBuffer? _buffer;
    private static ILogger Log => field ??= StaticLog.For(typeof(PttPreRoll));

    public static long Start()
    {
        AVAudioEngine? oldEngine;
        long token;
        lock (Lock) {
            (oldEngine, _engine, _format, _buffer) = (_engine, null, null, null);
            token = ++_lastToken;
        }
        // StopEngine and the native setup below are blocking calls - keep them outside the lock
        // so a concurrent TryTake()/Discard() (called from the app's own recorder startup, not
        // the PTT callback queue Start() runs on) never waits on them.
        StopEngine(oldEngine);

        try {
            var engine = new AVAudioEngine();
            var input = engine.InputNode;
            var format = input.GetBusOutputFormat(0);
            var sampleRate = (int)format.SampleRate;
            if (sampleRate <= 0) {
                Log.LogWarning("Pre-roll: the input node reports no sample rate");
                engine.Dispose();
                return 0;
            }

            var capacity = (int)(sampleRate * Constants.Audio.WalkieTalkiePreRollCapacity.TotalSeconds);
            var buffer = new PreRollBuffer(token, sampleRate, capacity);
            var frameLength = (uint)(sampleRate / 1000 * Constants.Audio.OpusFrameDurationMs);
            input.InstallTapOnBus(0, frameLength, format, (pcm, _) => buffer.TryAppend(pcm.AsReadOnlySpan()));
            engine.Prepare();
            engine.StartAndReturnError(out var error);
            if (error is not null) {
                Log.LogWarning("Pre-roll engine didn't start: {Error}", error.LocalizedDescription);
                input.RemoveTapOnBus(0);
                engine.Dispose();
                return 0;
            }

            // A concurrent TryTake()/Discard() may have already closed this token's window while
            // this method was doing native work on another thread - publish only if this is still
            // the newest, unclosed token, so a raced-out engine never ends up on the hardware
            // input node with no one left who will ever stop it.
            bool isPublished;
            lock (Lock) {
                isPublished = token == _lastToken && token > _closedToken;
                if (isPublished)
                    (_engine, _format, _buffer) = (engine, format, buffer);
            }
            if (!isPublished) {
                Log.LogWarning("Pre-roll: lost the race with a concurrent take/discard/start, dropping it");
                StopEngine(engine);
                return 0;
            }

            Log.LogInformation("Pre-roll capture started ({SampleRate} Hz)", sampleRate);
            return token;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Pre-roll capture failed to start");
            return 0;
        }
    }

    public static void Discard(long token)
    {
        AVAudioEngine? engine = null;
        lock (Lock) {
            if (token > _closedToken)
                _closedToken = token;
            if (_buffer is { } buffer && buffer.Token == token)
                (engine, _engine, _format, _buffer) = (_engine, null, null, null);
        }
        StopEngine(engine);
    }

    public static PreRollTake? TryTake()
    {
        AVAudioEngine? engine;
        PreRollBuffer? buffer;
        AVAudioFormat? format;
        lock (Lock) {
            // Closes the window for whatever token is newest right now, even if it's a Start()
            // still mid-flight on another thread and hasn't published yet - see Start().
            if (_lastToken > _closedToken)
                _closedToken = _lastToken;
            (engine, buffer, format) = (_engine, _buffer, _format);
            (_engine, _format, _buffer) = (null, null, null);
        }
        // Stopping here, before the caller starts AudioEngines.Recording, is the point: two
        // AVAudioEngine instances must never hold the hardware input node at once. It's a
        // blocking native call, so - like the drain below - it runs outside the lock.
        StopEngine(engine);
        if (buffer is null || format is null)
            return null;

        var minSampleCount = (int)(buffer.SampleRate * Constants.Audio.WalkieTalkiePreRollMinDuration.TotalSeconds);
        var samples = buffer.TryDrain(buffer.Token, minSampleCount);
        return samples is null ? null : new PreRollTake(samples, format);
    }

    // Private methods

    private static void StopEngine(AVAudioEngine? engine)
    {
        if (engine is null)
            return;

        try {
            engine.InputNode.RemoveTapOnBus(0);
            engine.Stop();
            engine.Dispose();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Pre-roll capture failed to stop cleanly");
        }
    }
}

public sealed record PreRollTake(float[] Samples, AVAudioFormat Format);
