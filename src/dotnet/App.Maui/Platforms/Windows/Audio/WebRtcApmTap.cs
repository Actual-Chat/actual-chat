using ActualLab.IO;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Records the exact frames <see cref="WindowsAudioCapture"/> hands the APM, so the
/// render/capture alignment can be replayed and measured offline. Enabled by creating
/// an <c>aec-tap.on</c> file in the app data directory. Dev builds only - it writes raw
/// microphone audio to disk, so the marker isn't even looked for in a production build.
/// </summary>
public sealed class WebRtcApmTap
{
    private const string MarkerFileName = "aec-tap.on";
    private const string OutputDirName = "AecTaps";
    private const int MaxDurationSeconds = 120;
    private const int FrameLength = Constants.Audio.RecordingSampleRate
        / 1000 * Constants.Audio.ApmFrameDurationMs * Constants.Audio.Channels;
    private const int SampleCapacity = Constants.Audio.RecordingSampleRate * MaxDurationSeconds;
    private const int FrameCapacity = MaxDurationSeconds * 1000 / Constants.Audio.ApmFrameDurationMs;

    private readonly float[] _mic = new float[SampleCapacity];
    private readonly float[] _reverse = new float[SampleCapacity];
    private readonly float[] _output = new float[SampleCapacity];
    private readonly long[] _frameElapsedTicks = new long[FrameCapacity];
    private readonly bool[] _frameHasReverse = new bool[FrameCapacity];
    private readonly CpuTimestamp _startedAt = CpuTimestamp.Now;
    private int _sampleCount;
    private int _frameCount;
    private ILogger Log { get; }

    public static WebRtcApmTap? TryStart(ILogger log)
    {
#if !IS_DEV_MAUI
        _ = log;
        return null;
#else
        try {
            if (!File.Exists((FilePath)FileSystem.AppDataDirectory & MarkerFileName))
                return null;

            log.LogWarning("APM tap is ON - recording up to {Duration}s of APM input", MaxDurationSeconds);
            return new WebRtcApmTap(log);
        }
        catch (Exception e) {
            log.LogWarning(e, "Failed to start the APM tap");
            return null;
        }
#endif
    }

    private WebRtcApmTap(ILogger log)
        => Log = log;

    public void Add(
        ReadOnlySpan<float> mic,
        ReadOnlySpan<float> reverse,
        ReadOnlySpan<float> output,
        bool hasReverse)
    {
        // Buffered in RAM on purpose: writing to disk from the capture loop would stall it,
        // causing the very sample drops this tap exists to detect.
        if (_frameCount >= FrameCapacity || _sampleCount + mic.Length > SampleCapacity)
            return;

        mic.CopyTo(_mic.AsSpan(_sampleCount));
        reverse.CopyTo(_reverse.AsSpan(_sampleCount));
        output.CopyTo(_output.AsSpan(_sampleCount));
        _sampleCount += mic.Length;
        _frameElapsedTicks[_frameCount] = _startedAt.Elapsed.Ticks;
        _frameHasReverse[_frameCount] = hasReverse;
        _frameCount++;
    }

    public void Stop()
    {
        // Reads the buffers unsynchronized, so the capture loop must already be drained.
        if (_sampleCount == 0)
            return;

        try {
            var dir = (FilePath)FileSystem.AppDataDirectory
                & OutputDirName
                & Invariant($"{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(dir);
            WriteWav(dir & "mic.wav", _mic.AsSpan(0, _sampleCount));
            WriteWav(dir & "reverse.wav", _reverse.AsSpan(0, _sampleCount));
            WriteWav(dir & "output.wav", _output.AsSpan(0, _sampleCount));
            WriteFrameLog(dir & "frames.tsv");
            Log.LogWarning("APM tap wrote {Duration:F1}s of audio to {Directory}",
                (double)_sampleCount / Constants.Audio.RecordingSampleRate,
                dir.Value);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to write the APM tap");
        }
    }

    // Private methods

    private void WriteFrameLog(FilePath path)
    {
        // expectedSamples vs. sampleIndex is what exposes capture drops: the mic stream is
        // paced by the device, so a shortfall means Windows discarded samples we never saw.
        using var writer = new StreamWriter(path.Value);
        writer.WriteLine("frame\telapsedMs\tsampleIndex\texpectedSamples\thasReverse");
        for (var i = 0; i < _frameCount; i++) {
            var elapsed = TimeSpan.FromTicks(_frameElapsedTicks[i]).TotalMilliseconds;
            var sampleIndex = (i + 1) * FrameLength;
            var expectedSamples = (long)(elapsed * Constants.Audio.RecordingSampleRate / 1000);
            writer.WriteLine(Invariant(
                $"{i}\t{elapsed:F2}\t{sampleIndex}\t{expectedSamples}\t{(_frameHasReverse[i] ? 1 : 0)}"));
        }
    }

    private static void WriteWav(FilePath path, ReadOnlySpan<float> samples)
    {
        var channels = Constants.Audio.Channels;
        var sampleRate = Constants.Audio.RecordingSampleRate;
        var dataSize = samples.Length * sizeof(float);
        using var stream = File.Create(path.Value);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(50 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(18);
        writer.Write((short)3); // WAVE_FORMAT_IEEE_FLOAT
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * sizeof(float));
        writer.Write((short)(channels * sizeof(float)));
        writer.Write((short)32);
        writer.Write((short)0);
        writer.Write("fact"u8);
        writer.Write(4);
        writer.Write(samples.Length / channels);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(MemoryMarshal.AsBytes(samples));
    }
}
