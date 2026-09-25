using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// One author's turn-taking numbers over one conversation, from entry timings only.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record CoachConversationAnalysis(
    [property: DataMember(Order = 0), Key(0)] ConversationId Id,
    [property: DataMember(Order = 1), Key(1)] AuthorId AuthorId,
    [property: DataMember(Order = 2), Key(2)] long Version = 0
    ) : IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(3)] public UserId UserId { get; init; }
    [DataMember, Key(4)] public long ConversationVersion { get; init; }
    [DataMember, Key(5)] public Moment EndsAt { get; init; }
    [DataMember, Key(6)] public double OwnSpeechSeconds { get; init; }
    [DataMember, Key(7)] public double TotalSpeechSeconds { get; init; }
    [DataMember, Key(8)] public int OwnTurns { get; init; }
    [DataMember, Key(9)] public int TotalTurns { get; init; }
    [DataMember, Key(10)] public int Participants { get; init; }
    [DataMember, Key(11)] public double LongestMonologueSeconds { get; init; }
    [DataMember, Key(12)] public int Responses { get; init; }
    [DataMember, Key(13)] public double ResponseGapSeconds { get; init; }
    [DataMember, Key(14)] public int Interruptions { get; init; }

    // This record relies on referential equality
    public bool Equals(CoachConversationAnalysis? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
