using ActualChat.Audio;
using ActualChat.Chat;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record CoachPaceDetails
{
    public static readonly CoachPaceDetails None = new();

    [DataMember, Key(0)] public SpeechPaceSummary? Summary { get; init; }
    [DataMember, Key(1)] public SpeechPaceDistribution Distribution { get; init; }
    [DataMember, Key(2)] public ApiArray<CoachPaceMoment> Moments { get; init; } = ApiArray<CoachPaceMoment>.Empty;
    [DataMember, Key(3)] public bool IsTruncated { get; init; }
    [DataMember, Key(4)] public double Slow { get; init; }
    [DataMember, Key(5)] public double Fast { get; init; }

    public static CoachPaceDetails FromRecords(IEnumerable<CoachRecord> records, double slow, double fast)
    {
        var summaries = new List<SpeechPaceSummary>();
        var moments = new List<CoachPaceMoment>();
        long below = 0, within = 0, above = 0;
        foreach (var record in records.OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.SourceId)) {
            if (record.IsExcluded || record.Entry?.Pace is not { } measurement)
                continue;

            summaries.Add(SpeechPaceSummary.FromMeasurement(measurement));
            var distribution = SpeechPaceHistogram.Classify(measurement.Analysis.Segments, slow, fast);
            below = checked(below + distribution.BelowMilliseconds);
            within = checked(within + distribution.WithinMilliseconds);
            above = checked(above + distribution.AboveMilliseconds);
            foreach (var segment in measurement.Analysis.Segments.Reverse()) {
                if (segment.WordsPerMinute >= slow && segment.WordsPerMinute <= fast
                    || moments.Count >= CoachOccurrence.MaxCount)
                    continue;

                var occurrence = new CoachOccurrence(record.ChatId, record.Entry.EntryLid,
                    segment.TextRange.Start, segment.TextRange.End - segment.TextRange.Start, record.OccurredAt);
                moments.Add(new CoachPaceMoment(occurrence, segment));
            }
        }
        return new CoachPaceDetails {
            Summary = SpeechPaceSummary.Merge(summaries),
            Distribution = new SpeechPaceDistribution(below, within, above),
            Moments = moments.ToApiArray(), Slow = slow, Fast = fast,
        };
    }
}

[DataContract, MessagePackObject]
public sealed partial record CoachPaceMoment(
    [property: DataMember, Key(0)] CoachOccurrence Occurrence,
    [property: DataMember, Key(1)] SpeechPaceSegment Segment);
