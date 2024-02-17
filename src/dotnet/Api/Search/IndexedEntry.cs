using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Search;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record IndexedEntry : IRequirementTarget
{
    public static readonly Requirement<IndexedEntry> MustBeValid = Requirement.New(
        new (() => StandardError.Constraint<IndexedEntry>("Not all fields set.")),
        (IndexedEntry? c) => c is { Id.IsNone: false, ChatId.IsNone: false } && !c.Content.IsNullOrEmpty());

    [DataMember, MemoryPackOrder(0)] public TextEntryId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Content { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public ChatId ChatId { get; init; }
}
