using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Translation(
    [property: DataMember, MemoryPackOrder(0)] TranslationId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
) : IHasId<TranslationId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(3)] public string Content { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public HashString SourceContentHash { get; init; }
    [DataMember, MemoryPackOrder(5)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(6)] public Moment ModifiedAt { get; init; }
    [DataMember, MemoryPackOrder(8)] public string StreamId { get; set; } = "";
    [DataMember, MemoryPackOrder(9)] public bool IsRealtime { get; set; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Language TargetLanguage => Id.Language;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsStreaming => !StreamId.IsNullOrEmpty();

    public bool MatchesOriginal(string originalContent)
        // we ask llm to ignore text already in the target language
        => Content.IsNullOrEmpty()
            || OrdinalIgnoreCaseEquals(Content, Constants.Translation.NoTranslationNeededText)
            || OrdinalIgnoreCaseEquals(originalContent, Content);

    // This record relies on referential equality
    public bool Equals(Translation? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
