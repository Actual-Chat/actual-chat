using ActualLab.IO;
using ActualChat.Audio.APM;

namespace Core.Audio.UnitTests;

public class ApmGainTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Manual")]
    public void ApmShouldAdjustGain()
    {
        WriteLine("Starting APM gain test...");

        // Arrange: load test PCM float32 (LE), 16 kHz, mono
        var filePath = GetAudioFilePath(new FilePath("micIn.bin"));
        var bytes = File.ReadAllBytes(filePath.ToString());
        var input = MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();

        // Set up APM similar to WindowsAudioCapture
        using var apm = new AudioProcessingModule(new StreamConfig(16000, 1), new StreamConfig(16000, 1));
        apm.Configure(cfg => cfg
            .EnableEchoCanceller(true)
            .EnableNoiseSuppression(true, NoiseSuppressionLevel.Moderate)
            .EnableAutomaticGainControl(true)
            .EnableHighPassFilter(true)
            .SetPipeline(false, false));

        var frameSize = 160; // 10 ms @ 16 kHz mono
        var totalFrames = input.Length / frameSize;
        var output = new float[totalFrames * frameSize];
        var loopBack = new float[frameSize];
        // Act: process frames through APM
        for (int i = 0; i < totalFrames; i++) {
            var inSpan = new ReadOnlySpan<float>(input, i * frameSize, frameSize);
            var outSpan = output.AsSpan(i * frameSize, frameSize);
            apm.AnalyzeReverseStream(loopBack);
            apm.ProcessStream(inSpan, outSpan);
            // var inSpanGain = AudioExt.ApproximateGain(inSpan);
            // var outSpanGain = AudioExt.ApproximateGain(outSpan);
            // WriteLine($"Processed frame {i + 1:0000}/{totalFrames}: {inSpanGain:F5} -> {outSpanGain:F5}");
        }

        // Write processed output to data/output.bin as float32 LE
        var outPath = GetAudioFilePath(new FilePath("output.bin"));
        Directory.CreateDirectory(outPath.DirectoryPath);
        var outBytes = MemoryMarshal.AsBytes(output.AsSpan());
        File.WriteAllBytes(outPath.ToString(), outBytes.ToArray());

        // Compute gains and peak amplitudes
        var inGain = AudioExt.ApproximateGain(input);
        var outGain = AudioExt.ApproximateGain(output);
        var inMax = MaxAbs(input);
        var outMax = MaxAbs(output);

        WriteLine("APM gain test completed. Original gain: " + inGain + ", processed gain: " + outGain);
        // Assert: processed output should have significantly higher gain
        outGain.Should().BeGreaterThan(inGain * 1.2, "APM AGC should increase overall gain");
        outMax.Should().BeGreaterThan((float)(inMax * 1.2), "APM AGC should increase peak amplitude");
    }

    private static float MaxAbs(ReadOnlySpan<float> data)
    {
        float m = 0f;
        foreach (var v in data)
            m = Math.Max(m, Math.Abs(v));
        return m;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
