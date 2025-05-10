using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Roulette;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record ChatRoulette(
    [property: DataMember, MemoryPackOrder(0)] ChatRouletteId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0)
    : IHasId<ChatRouletteId>, IHasVersion<long>
{
    public static readonly MediaId MediaId = MediaId.Parse("system-icons:chatroulette");

    [DataMember, MemoryPackOrder(2)] public ChatId ChatId { get; init; } = null!;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ProfileId1 => Id.ProfileId1;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ProfileId2 => Id.ProfileId2;

    // This record relies on referential equality
    public virtual bool Equals(ChatRoulette? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ChatRouletteFull(ChatRouletteId Id, long Version = 0)
    : ChatRoulette(Id, Version)
{
    [DataMember, MemoryPackOrder(3)] public UserId UserId1 { get; init; } = null!;
    [DataMember, MemoryPackOrder(4)] public UserId UserId2 { get; init; } = null!;
    [DataMember, MemoryPackOrder(5)] public UserId InitiatedBy { get; init; } = null!;
    [DataMember, MemoryPackOrder(6)] public UserId? CompletedBy { get; init; }
    [DataMember, MemoryPackOrder(7)] public CompleteChatRouletteReason CompleteReason { get; init; } = CompleteChatRouletteReason.None;
    [DataMember, MemoryPackOrder(8)] public Moment CompletedAt { get; init; }

    public ChatRoulette ToChatRoulette()
        => new (Id, Version) { ChatId = ChatId };

    // This record relies on referential equality
    public bool Equals(ChatRouletteFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public enum CompleteChatRouletteReason { None, Leave, Decline }

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatRouletteFullDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public ChatId? ChatId { get; init; }
    [DataMember, MemoryPackOrder(1)] public UserId? UserId1 { get; init; }
    [DataMember, MemoryPackOrder(2)] public UserId? UserId2 { get; init; }
    [DataMember, MemoryPackOrder(3)] public UserId? InitiatedBy { get; init; }
    [DataMember, MemoryPackOrder(4)] public Option<UserId?> CompletedBy { get; init; }
    [DataMember, MemoryPackOrder(5)] public CompleteChatRouletteReason? CompleteReason { get; init; }
}
