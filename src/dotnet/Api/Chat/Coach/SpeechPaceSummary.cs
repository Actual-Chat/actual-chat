using ActualChat.Audio;
using ActualChat.Serialization;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record SpeechPaceSummary
{
    private static readonly VersionedByteSerializer Serializer = new([MessagePackByteSerializer.Default]);

    [DataMember, Key(0)] public int Version { get; init; } = 1;
    [DataMember, Key(1)] public int BinWidth { get; init; } = 5;
    [DataMember, Key(2)] public ApiMap<int, long> Durations { get; init; } = new();
    [DataMember, Key(3)] public long MeasuredEntries { get; init; }
    [DataMember, Key(4)] public long AudioMilliseconds { get; init; }
    [DataMember, Key(5)] public long ValidWords { get; init; }
    [DataMember, Key(6)] public long RejectedWords { get; init; }
    [DataMember, Key(7)] public long UnclassifiedWords { get; init; }
    [DataMember, Key(8)] public long UnclassifiedMilliseconds { get; init; }
    [DataMember, Key(9)] public long PauseMilliseconds { get; init; }
    [DataMember, Key(10)] public long UnmappedMilliseconds { get; init; }

    public static SpeechPaceSummary FromMeasurement(SpeechPaceMeasurement measurement)
    {
        measurement.RequireValid();
        var analysis = measurement.Analysis;
        var histogram = SpeechPaceHistogram.Build(analysis.Segments);
        return new SpeechPaceSummary {
            BinWidth = histogram.BinWidth,
            Durations = new ApiMap<int, long>(histogram.Durations),
            MeasuredEntries = 1,
            AudioMilliseconds = measurement.AudioMilliseconds,
            ValidWords = analysis.ValidWords,
            RejectedWords = analysis.RejectedWords,
            UnclassifiedWords = analysis.UnclassifiedWords,
            UnclassifiedMilliseconds = analysis.UnclassifiedMilliseconds,
            PauseMilliseconds = analysis.PauseMilliseconds,
            UnmappedMilliseconds = analysis.UnmappedMilliseconds,
        }.RequireValid();
    }

    public static SpeechPaceSummary? Merge(IEnumerable<SpeechPaceSummary> summaries)
    {
        var result = new SpeechPaceSummary();
        var bins = new SortedDictionary<int, long>();
        foreach (var summary in summaries) {
            summary.RequireValid();
            foreach (var (bin, duration) in summary.Durations)
                bins[bin] = checked(bins.GetValueOrDefault(bin) + duration);
            result = result with {
                MeasuredEntries = checked(result.MeasuredEntries + summary.MeasuredEntries),
                AudioMilliseconds = checked(result.AudioMilliseconds + summary.AudioMilliseconds),
                ValidWords = checked(result.ValidWords + summary.ValidWords),
                RejectedWords = checked(result.RejectedWords + summary.RejectedWords),
                UnclassifiedWords = checked(result.UnclassifiedWords + summary.UnclassifiedWords),
                UnclassifiedMilliseconds = checked(result.UnclassifiedMilliseconds + summary.UnclassifiedMilliseconds),
                PauseMilliseconds = checked(result.PauseMilliseconds + summary.PauseMilliseconds),
                UnmappedMilliseconds = checked(result.UnmappedMilliseconds + summary.UnmappedMilliseconds),
            };
        }
        return result.MeasuredEntries == 0
            ? null
            : (result with { Durations = new ApiMap<int, long>(bins) }).RequireValid();
    }

    public byte[] ToBytes()
    {
        RequireValid();
        using var buffer = Serializer.Write(this);
        return buffer.ToArray();
    }

    public static SpeechPaceSummary FromBytes(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
            throw new InvalidDataException("Empty pace summary payload.");

        var value = (SpeechPaceSummary?)Serializer.Read(data, typeof(SpeechPaceSummary), out var length);
        if (value is null || length != data.Length)
            throw new InvalidDataException("Invalid pace summary payload.");

        return value.RequireValid();
    }

    public SpeechPaceSummary RequireValid()
    {
        if (Version != 1 || BinWidth != 5 || MeasuredEntries <= 0 || AudioMilliseconds <= 0
            || ValidWords < 0 || RejectedWords < 0 || UnclassifiedWords < 0 || UnclassifiedWords > ValidWords
            || UnclassifiedMilliseconds < 0 || PauseMilliseconds < 0 || UnmappedMilliseconds < 0
            || (Durations.Count == 0) != (ValidWords == UnclassifiedWords))
            throw new InvalidDataException("Unsupported or invalid pace summary.");

        var milliseconds = checked(UnclassifiedMilliseconds + PauseMilliseconds + UnmappedMilliseconds);
        foreach (var (bin, duration) in Durations) {
            if (bin < 0 || duration <= 0)
                throw new InvalidDataException("Invalid pace histogram bin.");

            milliseconds = checked(milliseconds + duration);
        }
        if (milliseconds != AudioMilliseconds)
            throw new InvalidDataException("Inconsistent pace summary coverage.");

        return this;
    }
}
