using ActualChat.Audio;
using ActualLab.IO;

namespace Core.Audio.UnitTests;

// Recordings are private, so they live outside the repo: 16 kHz mono s16 *.pcm files
// in the folder named by ActualChat_VadOnsetDataDir (default: tmp/vad-onset/pcm).

/// <summary>
/// Ranks voice-start pre-roll trims on real recordings replayed through the ONNX VAD:
/// every voice start is a case, scored by how far the trim's cut lands from the true speech onset.
/// </summary>
public sealed class VoicePreRollTrimFittingTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const int SampleRate = 16_000;
    private const int FrameLength = 320; // 20 ms
    private const int FrameMs = 20;
    private const int BufferFrameCount = 50; // 1 s
    private const int VadBatchLength = 512 * 3; // MAUI reads the VAD in 96 ms batches
    private const int LabelHop = 160; // 10 ms
    private const double ClipWeight = 2; // 1 ms late costs as much as 2 ms early
    private const double NearLeadMs = 300; // trims are fitted on onsets at most this far before the trigger
    private const int HighBandIndex = 3; // Bands from here up start fricatives

    private static readonly (double Low, double High)[] Bands =
        [(300, 600), (600, 1200), (1200, 2400), (2400, 4000), (4000, 7000)];

    [Fact(Skip = "Manual")]
    public async Task TrimsShouldBeRankedByOnsetError()
    {
        // arrange
        var repoRoot = FindRepoRoot();
        FilePath dataDir = Environment.GetEnvironmentVariable("ActualChat_VadOnsetDataDir")
            ?? repoRoot & "tmp" & "vad-onset" & "pcm";
        var modelPath = repoRoot & "src" & "dotnet" & "UI.Blazor.App"
            & "Components" & "AudioRecorder" & "workers" & "vad_batched.ort";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HostInfo());
        await using var sp = services.BuildServiceProvider();
        using var vad = new OnnxVoiceActivityDetector(sp, () => File.ReadAllBytesAsync(modelPath));
        await vad.EnsureInitialized();

        // act
        var cases = new List<OnsetCase>();
        foreach (var path in Directory.GetFiles(dataDir, "*.pcm").Order())
            cases.AddRange(CollectCases(vad, path));
        // pcm-soft holds clips cut from the end of one word to just past the next one, which starts with
        // a soft consonant; the VAD starts fresh on each, so the buffer before the trigger is under 1 s.
        var softCases = new List<OnsetCase>();
        var softDir = dataDir.DirectoryPath & "pcm-soft";
        if (Directory.Exists(softDir))
            foreach (var path in Directory.GetFiles(softDir, "*.pcm").Order())
                softCases.AddRange(CollectCases(vad, path, isShortLead: true).Take(1));
        var nearCases = cases.Where(c => Math.Max(c.LevelLeadMs, c.BandLeadMs) <= NearLeadMs).ToList();
        var farCases = cases.Except(nearCases).ToList();
        var rows = GetTrims()
            .Select(t => (t.Name, t.IsBaseline,
                Level: Score(nearCases, t.Keep, c => c.LevelLeadMs),
                Band: Score(nearCases, t.Keep, c => c.BandLeadMs),
                Soft: Score(softCases, t.Keep, c => c.BandLeadMs),
                Far: Score(farCases, t.Keep, c => c.BandLeadMs)))
            .OrderBy(r => r.Level.Loss + r.Band.Loss)
            .ToList();

        // assert
        cases.Should().NotBeEmpty();
        var caseLines = cases.Concat(softCases).Select(c => string.Join('\t',
            c.Name, F(c.TriggerSample / (double)SampleRate), F(c.LevelLeadMs), F(c.BandLeadMs),
            c.IsHighBandOnset ? "1" : "0"));
        await File.WriteAllLinesAsync(dataDir.DirectoryPath & "cases.tsv", caseLines);
        WriteCaseSummary(cases, nearCases, softCases);
        WriteLine($"{"trim",-44} | {"level",6} {"late%",6} {"late",6} {"early",6} | {"band",6} {"late%",6} "
            + $"{"late",6} {"p90late",7} {"early",6} | {"soft",6} {"late",6} | {"far",6} {"late",6}");
        foreach (var r in rows.Take(30).Concat(rows.Where(r => r.IsBaseline)).Distinct())
            WriteLine($"{r.Name,-44} | {F(r.Level.Loss),6} {F(r.Level.LateRate * 100),6} {F(r.Level.MeanLateMs),6} "
                + $"{F(r.Level.MeanEarlyMs),6} | {F(r.Band.Loss),6} {F(r.Band.LateRate * 100),6} "
                + $"{F(r.Band.MeanLateMs),6} {F(r.Band.P90LateMs),7} {F(r.Band.MeanEarlyMs),6} | "
                + $"{F(r.Soft.Loss),6} {F(r.Soft.MeanLateMs),6} | {F(r.Far.Loss),6} {F(r.Far.MeanLateMs),6}");
    }

    // Private methods

    private static IEnumerable<OnsetCase> CollectCases(
        OnnxVoiceActivityDetector vad,
        FilePath path,
        bool isShortLead = false)
    {
        var samples = ReadPcm16(path);
        var name = path.FileNameWithoutExtension.Value;
        vad.Reset();
        for (var offset = 0; offset + VadBatchLength <= samples.Length; offset += VadBatchLength) {
            vad.ConversationSignal();
            var results = vad.AppendChunk(samples.AsSpan(offset, VadBatchLength));
            if (!results.Any(r => r.Change is { Kind: VoiceActivityKind.Start }))
                continue;

            if (TryCreateCase(samples, offset + VadBatchLength, name, isShortLead) is { } onsetCase)
                yield return onsetCase;
        }
    }

    private static OnsetCase? TryCreateCase(float[] samples, int trigger, string name, bool isShortLead)
    {
        var bufferFrameCount = isShortLead ? Math.Min(BufferFrameCount, trigger / FrameLength) : BufferFrameCount;
        var bufferStart = trigger - bufferFrameCount * FrameLength;
        var labelEnd = trigger + 10 * LabelHop;
        if (bufferStart < 0 || labelEnd > samples.Length || HasZeroRun(samples, bufferStart, labelEnd))
            return null;

        // A short-lead clip starts at the gap before the word, so its background is the quietest part
        // of what precedes the trigger; a long-lead case has 400+ ms of background at the buffer start.
        var floorFrameCount = isShortLead ? bufferFrameCount * 2 : 40;
        var floorPercentile = isShortLead ? 0.1 : 0.2;
        var minOnsetFrame = isShortLead ? 3 : 40;

        // Two independent labels, both walking back from the trigger with 20 ms gaps bridged:
        // - level: broadband or pre-emphasized level 6 dB above the background of the oldest 400 ms;
        // - band: any of the Bands 9 dB above its own background, which catches quiet fricatives.
        var labelFrameCount = (labelEnd - bufferStart) / LabelHop;
        var bb = new double[labelFrameCount];
        var hf = new double[labelFrameCount];
        for (var i = 0; i < labelFrameCount; i++) {
            var frame = samples.AsSpan(bufferStart + i * LabelHop, LabelHop);
            bb[i] = Db(Rms(frame));
            hf[i] = Db(HighBandRms(frame));
        }
        var bbFloor = Percentile(bb.AsSpan(0, floorFrameCount), floorPercentile);
        var hfFloor = Percentile(hf.AsSpan(0, floorFrameCount), floorPercentile);
        var triggerFrame = (trigger - bufferStart) / LabelHop;
        var levelOnset = FindOnsetFrame(i => bb[i] > bbFloor + 6 || hf[i] > hfFloor + 6, triggerFrame);

        var bandDbs = GetBandRms(samples, bufferStart, labelFrameCount, LabelHop)
            .Select(band => band.Select(Db).ToArray())
            .ToArray();
        var bandFloors = bandDbs
            .Select(band => Percentile(band.AsSpan(0, floorFrameCount), floorPercentile))
            .ToArray();
        var bandOnset = FindOnsetFrame(i => IsAnyBandAbove(i, 9), triggerFrame);
        if (levelOnset is not { } level || level <= minOnsetFrame
            || bandOnset is not { } band || band <= minOnsetFrame)
            return null;

        var firstBand = Enumerable.Range(0, Bands.Length).First(b => bandDbs[b][band] > bandFloors[b] + 9);
        var gains = new double[bufferFrameCount];
        var hfGains = new double[bufferFrameCount];
        for (var i = 0; i < bufferFrameCount; i++) {
            var frame = samples.AsSpan(bufferStart + i * FrameLength, FrameLength);
            gains[i] = AudioExt.ApproximateGain(frame);
            hfGains[i] = HighBandRms(frame);
        }
        var bandGains = GetBandRms(samples, bufferStart, bufferFrameCount, FrameLength);
        var shippedKeep = VoiceOnsetFinder.GetSpeechFrameCount(
            samples.AsSpan(bufferStart, bufferFrameCount * FrameLength), FrameLength, SampleRate, 20);
        return new OnsetCase(name, trigger, (triggerFrame - level) * 10.0, (triggerFrame - band) * 10.0,
            firstBand >= HighBandIndex, gains, hfGains, bandGains, shippedKeep);

        bool IsAnyBandAbove(int frame, double marginDb) {
            for (var b = 0; b < Bands.Length; b++)
                if (bandDbs[b][frame] > bandFloors[b] + marginDb)
                    return true;

            return false;
        }
    }

    private static double[][] GetBandRms(float[] samples, int start, int frameCount, int frameLength)
    {
        // Returns per-frame RMS of each of the Bands, band-major
        var result = new double[Bands.Length][];
        for (var b = 0; b < Bands.Length; b++) {
            var filter = Biquad.NewBandPass(Bands[b].Low, Bands[b].High);
            var warmUpStart = Math.Max(0, start - 160);
            for (var i = warmUpStart; i < start; i++)
                filter.Next(samples[i]);
            result[b] = new double[frameCount];
            for (var f = 0; f < frameCount; f++) {
                var sum = 0.0;
                for (var i = 0; i < frameLength; i++) {
                    var y = filter.Next(samples[start + f * frameLength + i]);
                    sum += y * y;
                }
                result[b][f] = Math.Sqrt(sum / frameLength);
            }
        }
        return result;
    }

    private static int? FindOnsetFrame(Func<int, bool> isActive, int triggerFrame)
    {
        var last = triggerFrame - 1;
        while (last >= triggerFrame - 10 && !isActive(last))
            last--;
        if (last < triggerFrame - 10)
            return null;

        var onset = last;
        for (var i = last - 1; i >= 0 && onset - i <= 3; i--)
            if (isActive(i))
                onset = i;
        return onset;
    }

    private static TrimScore Score(List<OnsetCase> cases, Func<OnsetCase, int> keep, Func<OnsetCase, double> lead)
    {
        var errors = cases.Select(c => lead(c) - keep(c) * FrameMs).ToArray();
        if (errors.Length == 0)
            return new TrimScore(0, 0, 0, 0, 0);

        var lates = errors.Select(e => Math.Max(0, e)).ToArray();
        return new TrimScore(
            errors.Average(e => ClipWeight * Math.Max(0, e) + Math.Max(0, -e)),
            errors.Count(e => e > 10) / (double)errors.Length,
            lates.Average(),
            Percentile(lates, 0.9),
            errors.Average(e => Math.Max(0, -e)));
    }

    private static List<(string Name, bool IsBaseline, Func<OnsetCase, int> Keep)> GetTrims()
    {
        List<(string Name, bool IsBaseline, Func<OnsetCase, int> Keep)> trims = [
            ("V1 last/5, whole buffer", true, c => KeepV1(c.Gains)),
            ("V2 avg5/20, whole buffer", true, c => KeepV2(c.Gains)),
            ("V4 avg5/2, 300ms", true, c => KeepRatio(c.Gains, 2, 15, isBackOneFrame: true)),
            ("V5 avg5/3, 300ms (previous)", true, c => KeepRatio(c.Gains, 3, 15, isBackOneFrame: false)),
            ("V6 VoiceOnsetFinder, 400ms (shipped)", true, c => c.ShippedKeep),
        ];
        foreach (var k in new[] { 5, 8, 10, 12, 15 })
            trims.Add(($"fixed {k * FrameMs}ms", true, c => Math.Min(k, c.Gains.Length)));
        foreach (var limit in new[] { 15, 20, 25, 35 })
        foreach (var ratio in new[] { 2.0, 3, 5, 10, 20 })
            trims.Add(($"ratio avg5/{ratio}, {limit * FrameMs}ms", false,
                c => KeepRatio(c.Gains, ratio, limit, isBackOneFrame: false)));
        foreach (var limit in new[] { 15, 20, 25, 35 })
        foreach (var marginDb in new[] { 3.0, 4.5, 6, 9, 12 })
        foreach (var tolerance in new[] { 0, 1, 2 })
        foreach (var pad in new[] { 0, 1, 2 })
        foreach (var isHighBand in new[] { false, true }) {
            var floor = new FloorTrim(marginDb, tolerance, pad, limit, isHighBand);
            trims.Add((floor.ToString(), false, c => KeepAboveFloor(c, floor)));
        }
        foreach (var limit in new[] { 15, 20, 25, 35 })
        foreach (var marginDb in new[] { 4.5, 6, 9, 12 })
        foreach (var tolerance in new[] { 0, 1, 2 })
        foreach (var pad in new[] { 0, 1 })
        foreach (var firstBand in new[] { 0, 2 }) {
            var bandTrim = new BandTrim(marginDb, tolerance, pad, limit, firstBand);
            trims.Add((bandTrim.ToString(), false, c => KeepAboveBandFloor(c, bandTrim)));
        }
        return trims;
    }

    private static int KeepV1(double[] gains)
    {
        var speechGain = gains[^1];
        var start = gains.Length - 2;
        while (start > 0 && gains[start] >= speechGain / 5)
            start--;
        return gains.Length - Math.Clamp(start - 1, 0, gains.Length - 1);
    }

    private static int KeepV2(double[] gains)
    {
        var speechGain = gains[^Math.Min(5, gains.Length)..].Average();
        var start = gains.Length - 2;
        while (start > 0 && gains[start] >= speechGain / 20)
            start--;
        return gains.Length - Math.Clamp(start - 1, 0, Math.Max(0, gains.Length - 5));
    }

    private static int KeepRatio(double[] gains, double ratio, int limit, bool isBackOneFrame)
    {
        var n = gains.Length;
        var speechFrameCount = Math.Min((int)Math.Ceiling(limit / 3d), n);
        var speechGain = gains[^speechFrameCount..].Average();
        var minStart = Math.Max(0, n - limit);
        var start = n - speechFrameCount - 1;
        while (start >= minStart && gains[start] >= speechGain / ratio)
            start--;
        var discard = start < minStart ? minStart
            : isBackOneFrame ? start - 1
            : start;
        return n - Math.Clamp(discard, minStart, n - speechFrameCount);
    }

    private static int KeepAboveFloor(OnsetCase c, FloorTrim trim)
    {
        var n = c.Gains.Length;
        var factor = Math.Pow(10, trim.MarginDb / 20);
        var threshold = GetBackground(c.Gains, trim.Limit) * factor;
        var hfThreshold = GetBackground(c.HighBandGains, trim.Limit) * factor;
        var quietCount = 0;
        for (var i = n - 1; i >= Math.Max(0, n - trim.Limit); i--) {
            var isQuiet = c.Gains[i] < threshold && (!trim.IsHighBand || c.HighBandGains[i] < hfThreshold);
            quietCount = isQuiet ? quietCount + 1 : 0;
            if (quietCount > trim.Tolerance)
                return Math.Min(n - (i + quietCount) + trim.Pad, Math.Min(trim.Limit, n));
        }

        return Math.Min(trim.Limit, n);
    }

    private static int KeepAboveBandFloor(OnsetCase c, BandTrim trim)
    {
        var n = c.Gains.Length;
        var factor = Math.Pow(10, trim.MarginDb / 20);
        var thresholds = c.BandGains.Select(band => GetBackground(band, trim.Limit) * factor).ToArray();
        var quietCount = 0;
        for (var i = n - 1; i >= Math.Max(0, n - trim.Limit); i--) {
            var isQuiet = true;
            for (var b = trim.FirstBand; b < Bands.Length && isQuiet; b++)
                isQuiet = c.BandGains[b][i] < thresholds[b];
            quietCount = isQuiet ? quietCount + 1 : 0;
            if (quietCount > trim.Tolerance)
                return Math.Min(n - (i + quietCount) + trim.Pad, Math.Min(trim.Limit, n));
        }

        return Math.Min(trim.Limit, n);
    }

    private static double GetBackground(double[] levels, int limit)
    {
        // The frames older than the lookback window, or the quietest of all frames when the buffer is short
        var olderCount = levels.Length - limit;
        return olderCount >= 5
            ? Percentile(levels.AsSpan(0, olderCount), 0.3)
            : Percentile(levels, 0.1);
    }

    private void WriteCaseSummary(List<OnsetCase> cases, List<OnsetCase> nearCases, List<OnsetCase> softCases)
    {
        var leads = cases.Select(c => c.BandLeadMs).ToArray();
        var gaps = cases.Select(c => Math.Abs(c.BandLeadMs - c.LevelLeadMs)).ToArray();
        WriteLine($"cases: {cases.Count}, near: {nearCases.Count}, soft-consonant clips: {softCases.Count}");
        WriteLine($"band-label onset before trigger, ms: median {F(Percentile(leads, 0.5))}, "
            + $"p90 {F(Percentile(leads, 0.9))}, max {F(leads.Max())}, > 300ms: {leads.Count(l => l > 300)}");
        WriteLine($"|band - level| label gap, ms: median {F(Percentile(gaps, 0.5))}, p90 {F(Percentile(gaps, 0.9))}");
    }

    private static bool HasZeroRun(float[] samples, int start, int end)
    {
        var run = 0;
        for (var i = start; i < end; i++) {
            run = Math.Abs(samples[i]) < 2e-5f ? run + 1 : 0;
            if (run >= 160)
                return true;
        }

        return false;
    }

    private static double Rms(ReadOnlySpan<float> frame)
    {
        var sum = 0.0;
        foreach (var x in frame)
            sum += x * x;
        return Math.Sqrt(sum / frame.Length);
    }

    private static double HighBandRms(ReadOnlySpan<float> frame)
    {
        // Pre-emphasis keeps fricative energy (2-8 kHz) and drops most of the voiced low band
        var sum = 0.0;
        for (var i = 1; i < frame.Length; i++) {
            var y = frame[i] - 0.95 * frame[i - 1];
            sum += y * y;
        }
        return Math.Sqrt(sum / (frame.Length - 1));
    }

    private static double Db(double rms)
        => 20 * Math.Log10(rms + 1e-9);

    private static double Percentile(ReadOnlySpan<double> values, double p)
    {
        if (values.IsEmpty)
            return 0;

        var sorted = values.ToArray();
        Array.Sort(sorted);
        return sorted[(int)Math.Round(p * (sorted.Length - 1))];
    }

    private static string F(double value)
        => value.ToString("0.#", null);

    private static float[] ReadPcm16(FilePath path)
    {
        var bytes = File.ReadAllBytes(path);
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        return samples;
    }

    private static FilePath FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir != null && !File.Exists((FilePath)dir.FullName & "ActualChat.sln"))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found (ActualChat.sln).");
    }

    // Nested types

    private sealed record OnsetCase(
        string Name,
        int TriggerSample,
        double LevelLeadMs,
        double BandLeadMs,
        bool IsHighBandOnset,
        double[] Gains,
        double[] HighBandGains,
        double[][] BandGains,
        int ShippedKeep);

    private readonly record struct BandTrim(
        double MarginDb,
        int Tolerance,
        int Pad,
        int Limit,
        int FirstBand)
    {
        public override string ToString()
            => $"V6 bands{FirstBand}+ +{MarginDb}dB tol{Tolerance} pad{Pad}, {Limit * FrameMs}ms";
    }

    // RBJ band-pass with 0 dB peak gain
    private sealed class Biquad(double b0, double b2, double a1, double a2)
    {
        private double _x1, _x2, _y1, _y2;

        public static Biquad NewBandPass(double low, double high)
        {
            var center = Math.Sqrt(low * high);
            var w0 = 2 * Math.PI * center / SampleRate;
            var alpha = Math.Sin(w0) * (high - low) / (2 * center);
            var a0 = 1 + alpha;
            return new Biquad(alpha / a0, -alpha / a0, -2 * Math.Cos(w0) / a0, (1 - alpha) / a0);
        }

        public double Next(double x)
        {
            var y = b0 * x + b2 * _x2 - a1 * _y1 - a2 * _y2;
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            return y;
        }
    }

    private readonly record struct FloorTrim(
        double MarginDb,
        int Tolerance,
        int Pad,
        int Limit,
        bool IsHighBand)
    {
        public override string ToString()
            => $"floor+{MarginDb}dB tol{Tolerance} pad{Pad}{(IsHighBand ? " hf" : "")}, {Limit * FrameMs}ms";
    }

    private readonly record struct TrimScore(
        double Loss,
        double LateRate,
        double MeanLateMs,
        double P90LateMs,
        double MeanEarlyMs);
}
