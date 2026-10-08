using ActualChat.Audio;
using ActualChat.Serialization;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record SpeechPaceMeasurement(
    [property: DataMember, Key(0)] int Version,
    [property: DataMember, Key(1)] int AudioMilliseconds,
    [property: DataMember, Key(2)] SpeechPaceAnalysis Analysis)
{
    private static readonly VersionedByteSerializer Serializer = new([MessagePackByteSerializer.Default]);

    public byte[] ToBytes()
    {
        RequireValid();
        using var buffer = Serializer.Write(this);
        return buffer.ToArray();
    }

    public static SpeechPaceMeasurement FromBytes(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
            throw new InvalidDataException("Empty pace measurement payload.");

        var value = (SpeechPaceMeasurement?)Serializer.Read(data, typeof(SpeechPaceMeasurement), out var length);
        if (value is null || length != data.Length)
            throw new InvalidDataException("Invalid pace measurement payload.");

        return value.RequireValid();
    }

    public bool IsIdenticalTo(SpeechPaceMeasurement? other)
        => other is not null && ToBytes().AsSpan().SequenceEqual(other.ToBytes());

    public SpeechPaceMeasurement RequireValid()
    {
        if (Version != 1 || AudioMilliseconds <= 0 || Analysis is null || Analysis.Segments is null
            || Analysis.ValidWords < 0 || Analysis.RejectedWords < 0 || Analysis.UnclassifiedWords < 0
            || Analysis.UnclassifiedMilliseconds < 0 || Analysis.PauseMilliseconds < 0
            || Analysis.UnmappedMilliseconds < 0)
            throw new InvalidDataException("Unsupported or invalid pace measurement.");

        var previousTimeEnd = 0;
        var previousTextEnd = 0;
        long words = Analysis.UnclassifiedWords;
        long milliseconds = (long)Analysis.UnclassifiedMilliseconds + Analysis.PauseMilliseconds
            + Analysis.UnmappedMilliseconds;
        foreach (var segment in Analysis.Segments) {
            segment.RequireValid();
            if (segment.TimeRange.Start < previousTimeEnd || segment.TextRange.Start < previousTextEnd
                || segment.TimeRange.End > AudioMilliseconds)
                throw new InvalidDataException("Overlapping or out-of-bounds pace segments.");

            previousTimeEnd = segment.TimeRange.End;
            previousTextEnd = segment.TextRange.End;
            words += segment.Words;
            milliseconds += segment.DurationMilliseconds;
        }
        if (words != Analysis.ValidWords || milliseconds != AudioMilliseconds)
            throw new InvalidDataException("Inconsistent pace measurement coverage.");

        return this;
    }
}
