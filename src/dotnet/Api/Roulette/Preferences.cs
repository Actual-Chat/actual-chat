using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Preferences
{
    public static readonly Preferences Empty = new();

    [DataMember, MemoryPackOrder(0)] public Country? Country { get; init; }
    [DataMember, MemoryPackOrder(1)] public Gender Gender { get; init; } = Gender.Undefined;
    [DataMember, MemoryPackOrder(2)] public Language[] Languages { get; init; } = [];
    [DataMember, MemoryPackOrder(3)] public Interest[] Interests { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty =>
        Country is null
        && Gender == Gender.Undefined
        && Languages.Length == 0
        && Interests.Length == 0;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsSufficientForFiltering
        => !IsEmpty && Languages.Length > 0;

    // This record relies on referential equality
    public bool Equals(Preferences? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
