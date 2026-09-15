using ActualChat.Hashing;
using ActualLab.Versioning;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record UserVoice(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] long Version = 0
) : IHasVersion<long>
{
    [DataMember, Key(2)] public HashString SampleHash { get; init; }
    [DataMember, Key(3)] public string SonioxVoiceId { get => field ?? ""; init; } = "";
    [DataMember, Key(4)] public UserVoiceStatus Status { get; init; }
    [DataMember, Key(5)] public Moment? FailedUntil { get; init; }
    [DataMember, Key(6)] public Moment LastUsedAt { get; init; }
    [DataMember, Key(7)] public Moment CreatedAt { get; init; }
    [DataMember, Key(8)] public Moment ModifiedAt { get; init; }
}

[DataContract, MessagePackObject(true)]
public sealed partial record UserVoiceDiff : RecordDiff
{
    [DataMember] public HashString? SampleHash { get; init; }
    [DataMember] public string? SonioxVoiceId { get; init; }
    [DataMember] public UserVoiceStatus? Status { get; init; }
    [DataMember] public Option<Moment?> FailedUntil { get; init; }
    [DataMember] public Moment? LastUsedAt { get; init; }
    [DataMember] public Moment? CreatedAt { get; init; }
    [DataMember] public Moment? ModifiedAt { get; init; }
}
