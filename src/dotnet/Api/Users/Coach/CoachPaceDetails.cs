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
}

[DataContract, MessagePackObject]
public sealed partial record CoachPaceMoment(
    [property: DataMember, Key(0)] CoachOccurrence Occurrence,
    [property: DataMember, Key(1)] SpeechPaceSegment Segment);
