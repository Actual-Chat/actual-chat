using ActualChat.Audio;

namespace Core.Audio.UnitTests;

public class OnnxVoiceActivityDetectorTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Manual")]
    public async Task BasicTest()
    {
        // Find repository root (folder containing ActualChat.sln)
        var repoRoot = FindRepoRoot();
        Assert.True(Directory.Exists(repoRoot));

        // Resolve model and data paths
        var modelPath = Path.Combine(repoRoot,
            "src", "dotnet", "UI.Blazor.App", "Components", "AudioRecorder", "workers", "vad_batched.with_runtime_opt.ort");
        var dataPath = Path.Combine(repoRoot,
            "tests", "Core.Audio.UnitTests", "data", "micIn.bin");

        Assert.True(File.Exists(modelPath), $"Model file not found: {modelPath}");
        Assert.True(File.Exists(dataPath), $"Data file not found: {dataPath}");

        // Build minimal service provider with HostInfo and logging
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().AddConsole());
        services.AddSingleton(new HostInfo());
        var sp = services.BuildServiceProvider();

        // Create and initialize VAD
        using var vad = new OnnxVoiceActivityDetector(sp, () => File.ReadAllBytesAsync(modelPath));
        await vad.EnsureInitialized();
        Assert.True(vad.IsInitialized);

        // Read float32 mono PCM @ 16kHz and feed by 512-sample chunks
        var samples = ReadFloat32Le(dataPath);
        Assert.True(samples.Length >= 512);

        var chunkSize = 512 * 10; // 32ms window expected by detector
        var processed = 0;
        var eventCount = 0;
        for (var i = 0; i + chunkSize <= samples.Length; i += chunkSize) {
            var span = new ReadOnlySpan<float>(samples, i, chunkSize);
            var results = vad.AppendChunk(span);
            foreach (var result in results) {
                processed++;
                if (result.HasEvent)
                    eventCount++;
                var ms = i / 16000.0;
                WriteLine($"Chunk [{ms:F3}s]: {result.Change} ({result.Gain}) ");
            }
        }

        Assert.True(processed > 0);
        // We don't assert a specific number of events since it depends on the sample content and model,
        // but we verify the pipeline runs end-to-end without exceptions.
    }


    [Fact(Skip = "Manual")]
    public async Task InternalTest()
    {
        // Find repository root (folder containing ActualChat.sln)
        var repoRoot = FindRepoRoot();
        Assert.True(Directory.Exists(repoRoot));

        // Resolve model and data paths
        var modelPath = Path.Combine(repoRoot,
            "src", "dotnet", "UI.Blazor.App", "Components", "AudioRecorder", "workers", "vad_batched.with_runtime_opt.ort");
        var dataPath = Path.Combine(repoRoot,
            "tests", "Core.Audio.UnitTests", "data", "micIn.bin");

        Assert.True(File.Exists(modelPath), $"Model file not found: {modelPath}");
        Assert.True(File.Exists(dataPath), $"Data file not found: {dataPath}");

        // Build minimal service provider with HostInfo and logging
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().AddConsole());
        services.AddSingleton(new HostInfo());
        var sp = services.BuildServiceProvider();

        // Create and initialize VAD
        using var vad = new OnnxVoiceActivityDetector(sp, () => File.ReadAllBytesAsync(modelPath));
        await vad.EnsureInitialized();
        Assert.True(vad.IsInitialized);

        // Read float32 mono PCM @ 16kHz and feed by 512-sample chunks
        var samples = ReadFloat32Le(dataPath);
        Assert.True(samples.Length >= 512);

        var chunkSize = 512 * 5; // 32ms window expected by detector
        var processed = 0;
        for (var i = 0; i + chunkSize <= samples.Length; i += chunkSize) {
            var span = new ReadOnlySpan<float>(samples, i, chunkSize);
            var results = vad.AppendChunkInternal(span);
            if (results != null)
                processed += results.Length;

            if (results == null)
                continue;

            foreach (var result in results) {
                var ms = i / 16000.0;
                WriteLine($"Chunk [{ms:F3}s]: {result} ");
            }
        }

        Assert.True(processed > 0);
        // We don't assert a specific number of events since it depends on the sample content and model,
        // but we verify the pipeline runs end-to-end without exceptions.
    }

    [Theory(Skip = "Manual")]
    [InlineData("micIn.bin")]
    [InlineData("outAEC.bin")]
    [InlineData("outAEC_NC_AGC.bin")]
    public async Task ComboTest(string fileName)
    {
        // Find repository root (folder containing ActualChat.sln)
        var repoRoot = FindRepoRoot();
        Assert.True(Directory.Exists(repoRoot));

        // Resolve model and data paths
        var modelPath = Path.Combine(repoRoot,
            "src", "dotnet", "UI.Blazor.App", "Components", "AudioRecorder", "workers", "vad_batched.with_runtime_opt.ort");
        var dataPath = Path.Combine(repoRoot,
            "tests", "Core.Audio.UnitTests", "data", fileName);

        Assert.True(File.Exists(modelPath), $"Model file not found: {modelPath}");
        Assert.True(File.Exists(dataPath), $"Data file not found: {dataPath}");

        // Build minimal service provider with HostInfo and logging
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().AddConsole());
        services.AddSingleton(new HostInfo());
        var sp = services.BuildServiceProvider();

        // Create and initialize VAD
        using var vad1 = new OnnxVoiceActivityDetector(sp, () => File.ReadAllBytesAsync(modelPath));
        await vad1.EnsureInitialized();

        using var vad2 = new OnnxVoiceActivityDetector(sp, () => File.ReadAllBytesAsync(modelPath));
        await vad2.EnsureInitialized();

        // Read float32 mono PCM @ 16kHz and feed by 512-sample chunks
        var samples = ReadFloat32Le(dataPath);
        Assert.True(samples.Length >= 512);

        var sw = Stopwatch.StartNew();
        var chunkSize = 512 * 5; // 32ms window expected by detector
        var processed = 0;
        for (var i = 0; i + chunkSize <= samples.Length; i += chunkSize) {
            var span = new ReadOnlySpan<float>(samples, i, chunkSize);
            var internalResult = vad1.AppendChunkInternal(span);
            var result = vad2.AppendChunk(span);
            processed++;

            var ms = i / 16000.0;
            // WriteLine($"Chunk [{ms:F3}s]: {internalResult:F5} - {result.Gain:F5} change={result.Change?.Kind}");
        }

        Assert.True(processed > 0);
        sw.Stop();
        WriteLine($"Processed {processed} chunks in {sw.ElapsedMilliseconds} ms");
        // We don't assert a specific number of events since it depends on the sample content and model,
        // but we verify the pipeline runs end-to-end without exceptions.
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        for (int i = 0; i < 8 && dir != null; i++) {
            if (File.Exists(Path.Combine(dir.FullName, "ActualChat.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found (ActualChat.sln).");
    }

    private static float[] ReadFloat32Le(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = bytes.Length / 4;
        var floats = new float[count];
        Buffer.BlockCopy(bytes, 0, floats, 0, count * 4);
        if (!BitConverter.IsLittleEndian) {
            // Convert endianness if needed
            for (int i = 0; i < count; i++) {
                var b0 = bytes[i * 4 + 0];
                var b1 = bytes[i * 4 + 1];
                var b2 = bytes[i * 4 + 2];
                var b3 = bytes[i * 4 + 3];
                var val = BitConverter.ToSingle(new[] { b3, b2, b1, b0 }, 0);
                floats[i] = val;
            }
        }
        return floats;
    }
}
