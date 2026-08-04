using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ChatEntryLanguage(
    [property: DataMember(Order = 0), Key(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), Key(1)] long Version = 0
    ): IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public Language[] Languages { get; init; } = [];
    [DataMember, Key(3)] public DateTime CreatedAt { get; init; }
    [DataMember, Key(4)] public DateTime ModifiedAt { get; init; }
    [DataMember, Key(5)] public HashString EntryContentHash { get; set; } = HashString.None;

    // This record relies on referential equality
    public bool Equals(ChatEntryLanguage? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
