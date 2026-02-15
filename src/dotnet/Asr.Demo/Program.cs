using ActualChat.Asr;
using NAudio.Wave;
using static System.Console;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("AsrDemo");

var command = args.SingleOrDefault()?.ToLowerInvariant() ?? "mic";

// Download model
log.LogInformation("Ensuring model files are downloaded...");
var downloader = new ParakeetModelDownloader(log: log);
var modelFiles = await downloader.EnsureDownloadedAsync(useInt8: true).ConfigureAwait(true);
log.LogInformation("Model files ready");

// Load model
log.LogInformation("Loading model...");
using var model = new ParakeetModel(log);
var loadSw = Stopwatch.StartNew();
model.Load(modelFiles);
loadSw.Stop();
log.LogInformation("Model loaded in {Elapsed:F1}s", loadSw.Elapsed.TotalSeconds);

switch (command) {
case "file" when args.Length >= 2:
    TranscribeFile(args[1]);
    break;
case "mic":
    await TranscribeMicrophone().ConfigureAwait(true);
    break;
default:
    PrintUsage();
    break;
}

void PrintUsage()
{
    WriteLine("Parakeet ASR Demo");
    WriteLine("Usage:");
    WriteLine("  dotnet run -- file <path/to/audio.wav>    Transcribe a WAV file");
    WriteLine("  dotnet run -- mic                         Live microphone transcription");
}

void TranscribeFile(string filePath)
{
    if (!File.Exists(filePath)) {
        log.LogError("File not found: {Path}", filePath);
        return;
    }

    log.LogInformation("Reading: {Path}", filePath);
    var audio = LoadWavAs16KMono(filePath);
    var duration = (float)audio.Length / 16000;
    log.LogInformation("Audio: {Duration:F1}s ({Samples:N0} samples)", duration, audio.Length);

    log.LogInformation("Transcribing...");
    var sw = Stopwatch.StartNew();
    var result = model.Transcribe(audio);
    sw.Stop();

    var rtf = sw.Elapsed.TotalSeconds / duration;
    WriteLine();
    WriteLine($"Transcription ({sw.Elapsed.TotalSeconds:F2}s, RTF={rtf:F3}):");
    WriteLine(result.Text);
    WriteLine();

    if (result.Words.Count > 0) {
        WriteLine("Word timestamps:");
        foreach (var word in result.Words)
            WriteLine($"  [{word.StartTime:F2}s - {word.EndTime:F2}s] {word.Text}");
    }
}

async Task TranscribeMicrophone()
{
    if (!OperatingSystem.IsWindows()) {
        await Error.WriteLineAsync("Microphone capture via NAudio is only supported on Windows.").ConfigureAwait(true);
        return;
    }

    var sampleRate = 16000;
    var streaming = new ProgressiveStreamingHandler(model);

    // Accumulate audio buffer
    var audioBuffer = new List<float>();
    var bufferLock = new object();

    using var waveIn = new WaveInEvent();
    waveIn.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
    waveIn.BufferMilliseconds = 100;

    waveIn.DataAvailable += (_, e) => {
        var floatCount = e.BytesRecorded / 4;
        var samples = new float[floatCount];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
        lock (bufferLock)
            audioBuffer.AddRange(samples);
    };

    WriteLine("Starting microphone capture (16kHz mono)...");
    WriteLine("Speak into the microphone. Press Ctrl+C to stop.");
    WriteLine();

    using var cts = new CancellationTokenSource();
    CancelKeyPress += (_, e) => {
        e.Cancel = true;
        // ReSharper disable once AccessToDisposedClosure
        cts.Cancel();
    };

    waveIn.StartRecording();

    var lastFixedText = "";
    var lastActiveText = "";
    var interval = TimeSpan.FromMilliseconds(250);

    try {
        while (!cts.Token.IsCancellationRequested) {
            await Task.Delay(interval, cts.Token).ConfigureAwait(true);

            float[] audioSnapshot;
            lock (bufferLock)
                audioSnapshot = [.. audioBuffer];

            if (audioSnapshot.Length < sampleRate / 2)
                continue;

            // Simple VAD: check if max amplitude in last 2s is above threshold
            var vadWindowSamples = Math.Min(sampleRate * 2, audioSnapshot.Length);
            var maxAmplitude = 0f;
            for (int i = audioSnapshot.Length - vadWindowSamples; i < audioSnapshot.Length; i++) {
                var abs = Math.Abs(audioSnapshot[i]);
                if (abs > maxAmplitude)
                    maxAmplitude = abs;
            }

            if (maxAmplitude < 0.01f)
                continue;

            var sw = Stopwatch.StartNew();
            var partial = streaming.TranscribeIncremental(audioSnapshot);
            sw.Stop();

            var latencyMs = (int)sw.Elapsed.TotalMilliseconds;
            var audioDur = (float)audioSnapshot.Length / sampleRate;
            var rtf = sw.Elapsed.TotalSeconds / Math.Max(0.001, audioDur);
            var speed = (int)(1.0 / Math.Max(0.001, rtf));

            // Print new fixed text increments as permanent lines
            if (!string.Equals(partial.FixedText, lastFixedText, StringComparison.Ordinal) && !string.IsNullOrEmpty(partial.FixedText)) {
                var newPart = partial.FixedText.Length > lastFixedText.Length
                    ? partial.FixedText[lastFixedText.Length..].TrimStart()
                    : partial.FixedText;
                Console.Write($"\r\x1b[2K{newPart}\n");
                lastFixedText = partial.FixedText;
                lastActiveText = ""; // force active redraw
            }

            // Update active text tail on current line
            if (!string.Equals(partial.ActiveText, lastActiveText, StringComparison.Ordinal)) {
                lastActiveText = partial.ActiveText;
                var tail = lastActiveText.Length > 60
                    ? "..." + lastActiveText[^57..]
                    : lastActiveText;
                Console.Write($"\r\x1b[2K\x1b[36m{tail}\x1b[0m \x1b[90m[x{speed}/{latencyMs}ms]\x1b[0m");
            }
        }
    }
    catch (OperationCanceledException) {
        // Expected
    }

    waveIn.StopRecording();
    WriteLine();
    WriteLine();

    // Final transcription
    float[] finalAudio;
    lock (bufferLock)
        finalAudio = [.. audioBuffer];

    if (finalAudio.Length > sampleRate / 2) {
        var finalText = streaming.Finalize(finalAudio);
        WriteLine("Final transcript:");
        WriteLine(finalText);
    }
}

static float[] LoadWavAs16KMono(string path)
{
    using var reader = new AudioFileReader(path);

    // Resample to 16kHz mono if needed
    var needsResample = reader.WaveFormat.SampleRate != 16000;
    var needsMono = reader.WaveFormat.Channels > 1;

    ISampleProvider provider = reader;
    if (needsMono)
        provider = provider.ToMono();

    if (needsResample) {
        // Use WdlResampler through MediaFoundationResampler for WAV files
        // Simple approach: read all samples, then resample manually
        var allSamples = ReadAllSamples(provider);
        return ResampleSimple(allSamples, reader.WaveFormat.SampleRate, 16000);
    }

    return ReadAllSamples(provider);
}

static float[] ReadAllSamples(ISampleProvider provider)
{
    var samples = new List<float>();
    var buffer = new float[16000]; // 1 second chunks
    int read;
    while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        samples.AddRange(buffer.AsSpan(0, read).ToArray());
    return [.. samples];
}

static float[] ResampleSimple(float[] input, int fromRate, int toRate)
{
    if (fromRate == toRate)
        return input;

    var ratio = (double)toRate / fromRate;
    var outputLength = (int)(input.Length * ratio);
    var output = new float[outputLength];

    for (int i = 0; i < outputLength; i++) {
        var srcPos = i / ratio;
        var srcIdx = (int)srcPos;
        var frac = (float)(srcPos - srcIdx);

        if (srcIdx + 1 < input.Length)
            output[i] = input[srcIdx] * (1 - frac) + input[srcIdx + 1] * frac;
        else if (srcIdx < input.Length)
            output[i] = input[srcIdx];
    }

    return output;
}
