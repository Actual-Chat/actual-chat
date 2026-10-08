using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ActualChat.Chat;
using ActualChat.Module;
using ActualChat.Serialization;
using ActualChat.Users;
using ActualLab.IO;
using BenchmarkDotNet.Attributes;

namespace ActualChat.Benchmarks;

[Config(typeof(InProcessShortRunConfig))]
[MemoryDiagnoser]
public class SpeechPaceStorageBenchmarks
{
    private VersionedByteSerializer _serializer = null!;
    private VersionedByteSerializer _compressedSerializer = null!;
    private SpeechPaceStorageData _sample = null!;
    private byte[] _json = [];
    private byte[] _messagePack = [];
    private byte[] _lz4 = [];
    private byte[] _packed = [];
    private SpeechPaceMeasurement? _measurement;
    private byte[] _measurementBytes = [];
    private SpeechPaceSummary? _summary;
    private byte[] _summaryBytes = [];
    private SpeechPaceSummary[] _summaryItems = [];

    [Params(6, 60)]
    public int SegmentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        CoreModuleInitializer.Initialize();
        _serializer = new VersionedByteSerializer([MessagePackByteSerializer.Default]);
        var compressed = new MessagePackByteSerializer(
            MessagePackByteSerializer.DefaultOptions.WithCompression(MessagePackCompression.Lz4BlockArray));
        _compressedSerializer = new VersionedByteSerializer([MessagePackByteSerializer.Default, compressed]);
        _sample = CreateSample(SegmentCount);
        _measurement = CreateMeasurement(_sample);
        _measurementBytes = EncodeMeasurement();
        _summary = _measurement is null ? null : SpeechPaceSummary.FromMeasurement(_measurement);
        _summaryBytes = EncodeSummary();
        _summaryItems = _summary is null ? [] : Enumerable.Repeat(_summary, 100).ToArray();
        _json = EncodeJson();
        _messagePack = EncodeMessagePack();
        _lz4 = EncodeLz4();
        _packed = EncodePacked();
        foreach (var result in new[] { DecodeJson(), DecodeMessagePack(), DecodeLz4(), DecodePacked() })
            if (result with { Segments = _sample.Segments } != _sample
                || !result.Segments.SequenceEqual(_sample.Segments))
                throw new InvalidOperationException("Storage prototype round trip failed.");
    }

    [Benchmark]
    public byte[] EncodeJson()
        => Encoding.UTF8.GetBytes(SystemJsonSerializer.Default.Write(_sample));

    [Benchmark]
    public SpeechPaceStorageData DecodeJson()
        => SystemJsonSerializer.Default.Read<SpeechPaceStorageData>(Encoding.UTF8.GetString(_json));

    [Benchmark]
    public byte[] EncodeMessagePack()
    {
        using var buffer = _serializer.Write(_sample);
        return buffer.WrittenSpan.ToArray();
    }

    [Benchmark]
    public SpeechPaceStorageData DecodeMessagePack()
        => (SpeechPaceStorageData)_serializer.Read(_messagePack, typeof(SpeechPaceStorageData), out _)!;

    [Benchmark]
    public byte[] EncodeLz4()
    {
        using var buffer = _compressedSerializer.Write(_sample);
        return buffer.WrittenSpan.ToArray();
    }

    [Benchmark]
    public SpeechPaceStorageData DecodeLz4()
        => (SpeechPaceStorageData)_compressedSerializer.Read(_lz4, typeof(SpeechPaceStorageData), out _)!;

    [Benchmark]
    public byte[] EncodePacked()
        => EncodePackedData(_sample);

    [Benchmark]
    public SpeechPaceStorageData DecodePacked()
        => DecodePackedData(_packed);

    [Benchmark]
    public byte[] EncodeMeasurement()
        => _measurement?.ToBytes() ?? [];

    [Benchmark]
    public SpeechPaceMeasurement? DecodeMeasurement()
        => _measurementBytes.Length == 0 ? null : SpeechPaceMeasurement.FromBytes(_measurementBytes);

    [Benchmark]
    public byte[] EncodeSummary()
        => _summary?.ToBytes() ?? [];

    [Benchmark]
    public SpeechPaceSummary? DecodeSummary()
        => _summaryBytes.Length == 0 ? null : SpeechPaceSummary.FromBytes(_summaryBytes);

    [Benchmark]
    public SpeechPaceSummary? MergeSummaries()
        => SpeechPaceSummary.Merge(_summaryItems);

    public static SpeechPaceStorageData CreateSample(int segments)
    {
        var items = new SpeechPaceStorageSegment[segments];
        var text = 0;
        var time = 0;
        for (var i = 0; i < segments; i++) {
            var words = 18 + i % 17;
            var textEnd = text + words * (4 + i % 4);
            var duration = 9_000 + i % 5 * 500;
            items[i] = new SpeechPaceStorageSegment(text, textEnd, time, time + duration, words, duration);
            text = textEnd + 1;
            time += duration + (i % 3 == 0 ? 1_500 : 100);
        }
        return new SpeechPaceStorageData(1, "en", time, 1, items);
    }

    public static byte[] EncodePackedData(SpeechPaceStorageData sample)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)0);
        writer.Write7BitEncodedInt(sample.AlgorithmVersion);
        writer.Write(sample.Language);
        writer.Write7BitEncodedInt(sample.RecordingMilliseconds);
        writer.Write(sample.TimingQuality);
        writer.Write7BitEncodedInt(sample.Segments.Length);
        var previousTextEnd = 0;
        var previousTimeEnd = 0;
        foreach (var segment in sample.Segments) {
            writer.Write7BitEncodedInt(segment.TextStart - previousTextEnd);
            writer.Write7BitEncodedInt(segment.TextEnd - segment.TextStart);
            writer.Write7BitEncodedInt(segment.StartMilliseconds - previousTimeEnd);
            writer.Write7BitEncodedInt(segment.EndMilliseconds - segment.StartMilliseconds);
            writer.Write7BitEncodedInt(segment.Words);
            writer.Write7BitEncodedInt(segment.SpeechMilliseconds);
            previousTextEnd = segment.TextEnd;
            previousTimeEnd = segment.EndMilliseconds;
        }
        writer.Flush();
        return stream.ToArray();
    }

    public static SpeechPaceStorageData DecodePackedData(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadByte() != 0)
            throw new InvalidDataException("Unsupported packed prototype version.");

        var version = reader.Read7BitEncodedInt();
        var language = reader.ReadString();
        var recording = reader.Read7BitEncodedInt();
        var quality = reader.ReadByte();
        var count = reader.Read7BitEncodedInt();
        if (count < 0 || count > data.Length / 6)
            throw new InvalidDataException("Invalid packed segment count.");

        var segments = new SpeechPaceStorageSegment[count];
        var previousTextEnd = 0;
        var previousTimeEnd = 0;
        for (var i = 0; i < count; i++) {
            var textStart = checked(previousTextEnd + reader.Read7BitEncodedInt());
            var textEnd = checked(textStart + reader.Read7BitEncodedInt());
            var start = checked(previousTimeEnd + reader.Read7BitEncodedInt());
            var end = checked(start + reader.Read7BitEncodedInt());
            var words = reader.Read7BitEncodedInt();
            var speech = reader.Read7BitEncodedInt();
            segments[i] = new SpeechPaceStorageSegment(textStart, textEnd, start, end, words, speech);
            previousTextEnd = textEnd;
            previousTimeEnd = end;
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing packed prototype bytes.");

        return new SpeechPaceStorageData(version, language, recording, quality, segments);
    }

    public static void Report()
    {
        CoreModuleInitializer.Initialize();
        foreach (var count in new[] { 0, 1, 6, 60, 360 }) {
            var benchmark = new SpeechPaceStorageBenchmarks { SegmentCount = count };
            benchmark.Setup();
            var sample = CreateSample(count);
            var samples = new Dictionary<string, byte[]> {
                ["json"] = benchmark._json,
                ["messagePack"] = benchmark._messagePack,
                ["lz4"] = benchmark._lz4,
                ["packed"] = benchmark._packed,
            };
            var formats = samples.Select(x => new {
                Format = x.Key,
                Bytes = x.Value.Length,
                BrotliBytes = Compress(x.Value).Length,
                Base64 = Convert.ToBase64String(x.Value),
            });
            var report = new {
                Segments = count,
                Sample = sample,
                Formats = formats,
                Histogram = HistogramSizes(sample),
                Measurement = MeasurementSize(sample),
                DailySummary = SummarySize(sample),
            };
            Console.WriteLine(SystemJsonSerializer.Default.Write(report));
        }
    }

    public static void ReportCorpus(FilePath path)
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(path.Value));
        var count = 0;
        var probeEntries = 0;
        var rejectedWords = 0;
        var unclassifiedWords = 0;
        var segments = 0;
        var classifiedMilliseconds = 0L;
        var recordingMilliseconds = 0L;
        var provenanceEligibleEntries = 0;
        foreach (var entry in corpus.RootElement.EnumerateArray()) {
            var text = entry.GetProperty("content").GetString()!;
            var points = entry.GetProperty("timeMap").GetProperty("Data")
                .EnumerateArray().Select(x => x.GetSingle()).ToArray();
            var markup = new PlayableTextMarkup(text, new LinearMap(points));
            var duration = entry.GetProperty("durationSeconds").GetDouble();
            var language = Language.TryParse(entry.GetProperty("language").GetString(), true);
            var probe = SpeechPaceStats.Compute(markup, duration, language, hasDetailedTiming: true);
            var eligible = SpeechPaceStats.Compute(markup, duration, language, hasDetailedTiming: false);
            count++;
            recordingMilliseconds += (long)Math.Round(duration * 1_000);
            if (eligible is not null)
                provenanceEligibleEntries++;
            if (probe is null)
                continue;

            probeEntries++;
            rejectedWords += probe.RejectedWords;
            unclassifiedWords += probe.UnclassifiedWords;
            segments += probe.Segments.Length;
            classifiedMilliseconds += probe.Segments.Sum(x => (long)x.DurationMilliseconds);
        }
        Console.WriteLine(SystemJsonSerializer.Default.Write(new {
            Entries = count,
            ProbeEntries = probeEntries,
            ProvenanceEligibleEntries = provenanceEligibleEntries,
            ProbeRejectedWords = rejectedWords,
            ProbeUnclassifiedWords = unclassifiedWords,
            ProbeSegments = segments,
            ProbeClassifiedMilliseconds = classifiedMilliseconds,
            RecordingMilliseconds = recordingMilliseconds,
        }));
    }

    public static void ReportExisting(FilePath path)
    {
        CoreModuleInitializer.Initialize();
        using var data = JsonDocument.Parse(File.ReadAllText(path.Value));
        var events = data.RootElement.GetProperty("events").EnumerateArray()
            .Select(x => SystemJsonSerializer.Default.Read<CoachRecord>(x.GetRawText())).ToArray();
        var days = data.RootElement.GetProperty("days").EnumerateArray()
            .Select(x => SystemJsonSerializer.Default.Read<CoachDay>(x.GetRawText())).ToArray();
        var spans = data.RootElement.GetProperty("spans").EnumerateArray()
            .Select(x => SystemJsonSerializer.Default.Read<SpeechSpan[]>(x.GetRawText())).ToArray();
        Console.WriteLine(SystemJsonSerializer.Default.Write(new {
            EntryEvents = StorageTotals(events.Where(x => x.Kind == CoachRecordKind.Entry).ToArray()),
            RunEvents = StorageTotals(events.Where(x => x.Kind == CoachRecordKind.Run).ToArray()),
            Days = StorageTotals(days),
            ChatSpans = StorageTotals(spans),
        }));
    }

    // Private methods

    private static object StorageTotals<T>(T[] values)
        where T : class
    {
        var serializer = new VersionedByteSerializer([MessagePackByteSerializer.Default]);
        var jsonBytes = 0L;
        var binaryBytes = 0L;
        foreach (var value in values) {
            var json = SystemJsonSerializer.Default.Write(value);
            using var binary = serializer.Write(value);
            var restored = (T)serializer.Read(binary.WrittenMemory, typeof(T), out var readLength)!;
            using var before = JsonDocument.Parse(json);
            using var after = JsonDocument.Parse(SystemJsonSerializer.Default.Write(restored));
            if (readLength != binary.WrittenCount || !JsonElement.DeepEquals(before.RootElement, after.RootElement))
                throw new InvalidOperationException($"Existing {typeof(T).Name} binary round trip failed.");

            jsonBytes += Encoding.UTF8.GetByteCount(json);
            binaryBytes += binary.WrittenCount;
        }
        return new {
            Rows = values.Length,
            JsonBytes = jsonBytes,
            MessagePackBytes = binaryBytes,
            AverageJsonBytes = values.Length > 0 ? (double)jsonBytes / values.Length : (double?)null,
            AverageMessagePackBytes = values.Length > 0 ? (double)binaryBytes / values.Length : (double?)null,
        };
    }

    private static object HistogramSizes(SpeechPaceStorageData sample)
    {
        var segments = sample.Segments.Select(x => new ActualChat.Audio.SpeechPaceSegment(
            (x.TextStart, x.TextEnd), (x.StartMilliseconds, x.EndMilliseconds), x.Words)).ToArray();
        var histogram = ActualChat.Audio.SpeechPaceHistogram.Build(segments);
        var first = histogram.Durations.Keys.FirstOrDefault();
        var last = histogram.Durations.Keys.LastOrDefault();
        var dense = histogram.Durations.Count == 0 ? [] : new long[last - first + 1];
        foreach (var (bin, duration) in histogram.Durations)
            dense[bin - first] = duration;

        var sparsePayload = Tuple.Create(1, sample.Language, histogram.BinWidth, histogram.Durations);
        var densePayload = Tuple.Create(1, sample.Language, histogram.BinWidth, first, dense);
        var serializer = new VersionedByteSerializer([MessagePackByteSerializer.Default]);
        using var sparseBytes = serializer.Write(sparsePayload);
        using var denseBytes = serializer.Write(densePayload);
        return new {
            OccupiedBins = histogram.Durations.Count,
            DenseBins = dense.Length,
            TotalMilliseconds = histogram.TotalMilliseconds,
            SparseMessagePackBytes = sparseBytes.WrittenCount,
            DenseMessagePackBytes = denseBytes.WrittenCount,
            SparseJsonBytes = Encoding.UTF8.GetByteCount(SystemJsonSerializer.Default.Write(sparsePayload)),
            DenseJsonBytes = Encoding.UTF8.GetByteCount(SystemJsonSerializer.Default.Write(densePayload)),
        };
    }

    private static SpeechPaceMeasurement? CreateMeasurement(SpeechPaceStorageData sample)
    {
        if (sample.RecordingMilliseconds <= 0)
            return null;

        var segments = sample.Segments.Select(x => new ActualChat.Audio.SpeechPaceSegment(
            (x.TextStart, x.TextEnd), (x.StartMilliseconds, x.EndMilliseconds), x.Words)).ToArray();
        var analysis = new ActualChat.Audio.SpeechPaceAnalysis(segments, segments.Sum(x => x.Words), 0, 0, 0, 0,
            sample.RecordingMilliseconds - segments.Sum(x => x.DurationMilliseconds));
        return new SpeechPaceMeasurement(1, sample.RecordingMilliseconds, analysis);
    }

    private static object? MeasurementSize(SpeechPaceStorageData sample)
    {
        var measurement = CreateMeasurement(sample);
        if (measurement is null)
            return null;

        var binary = measurement.ToBytes();
        if (!SpeechPaceMeasurement.FromBytes(binary).IsIdenticalTo(measurement))
            throw new InvalidOperationException("Production pace measurement round trip failed.");

        return new {
            JsonBytes = Encoding.UTF8.GetByteCount(SystemJsonSerializer.Default.Write(measurement)),
            MessagePackBytes = binary.Length,
            Base64 = Convert.ToBase64String(binary),
        };
    }

    private static object? SummarySize(SpeechPaceStorageData sample)
    {
        var measurement = CreateMeasurement(sample);
        if (measurement is null)
            return null;

        var summary = SpeechPaceSummary.FromMeasurement(measurement);
        var binary = summary.ToBytes();
        var restored = SpeechPaceSummary.FromBytes(binary);
        if (!restored.ToBytes().AsSpan().SequenceEqual(binary))
            throw new InvalidOperationException("Daily pace summary round trip failed.");

        var day = new CoachDay(Moment.EpochStart) {
            Language = "en",
            Entries = 1,
            Words = checked((int)summary.ValidWords),
            DurationSeconds = summary.AudioMilliseconds / 1_000d,
            SpeechSeconds = summary.Durations.Values.Sum() / 1_000d,
        };
        var dayData = Encoding.UTF8.GetBytes(SystemJsonSerializer.Default.Write(day));
        return new {
            OccupiedBins = summary.Durations.Count,
            DayBase64 = Convert.ToBase64String(dayData),
            JsonBytes = Encoding.UTF8.GetByteCount(SystemJsonSerializer.Default.Write(summary)),
            MessagePackBytes = binary.Length,
            Base64 = Convert.ToBase64String(binary),
        };
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var compressor = new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true))
            compressor.Write(bytes);
        return stream.ToArray();
    }
}

[DataContract, MessagePackObject]
public sealed partial record SpeechPaceStorageData(
    [property: DataMember, Key(0)] int AlgorithmVersion,
    [property: DataMember, Key(1)] string Language,
    [property: DataMember, Key(2)] int RecordingMilliseconds,
    [property: DataMember, Key(3)] byte TimingQuality,
    [property: DataMember, Key(4)] SpeechPaceStorageSegment[] Segments);

[DataContract, MessagePackObject]
public sealed partial record SpeechPaceStorageSegment(
    [property: DataMember, Key(0)] int TextStart,
    [property: DataMember, Key(1)] int TextEnd,
    [property: DataMember, Key(2)] int StartMilliseconds,
    [property: DataMember, Key(3)] int EndMilliseconds,
    [property: DataMember, Key(4)] int Words,
    [property: DataMember, Key(5)] int SpeechMilliseconds);
