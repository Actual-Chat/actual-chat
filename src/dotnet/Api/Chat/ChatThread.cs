using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByIdAndVersionParameterComparer<ChatId, long>))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatThread(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public string Title { get; init; } = "";

    // This record relies on referential equality
    public bool Equals(ChatThread? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

