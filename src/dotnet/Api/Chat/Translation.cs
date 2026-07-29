using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Represents a translation of chat content to a target language.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Translation(
    [property: DataMember, MemoryPackOrder(0), Key(0)] TranslationId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0
) : IHasId<TranslationId>, IHasVersion<long>, IRequirementTarget, ISanitized
{
    [DataMember, MemoryPackOrder(2), Key(2)]
    public string Content {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember, MemoryPackOrder(3), Key(3)] public HashString SourceContentHash { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public Moment ModifiedAt { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public StreamId? StreamId { get; set; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Language TargetLanguage => Id.Language;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsStreaming => StreamId is not null;

    public bool MatchesOriginal(string originalContent)
        // we ask llm to ignore text already in the target language
        => Content.IsNullOrEmpty()
            || string.Equals(Content, Constants.Translation.NoTranslationNeededText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(originalContent, Content, StringComparison.OrdinalIgnoreCase);

    // This record relies on referential equality
    public bool Equals(Translation? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>
/// Represents changes to a <see cref="Translation"/> for incremental updates.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record TranslationDiff : RecordDiff, ISanitized
{
    [DataMember, MemoryPackOrder(0)] public long? Version { get; init; }
    [DataMember, MemoryPackOrder(1)] public string? Content {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    }
    [DataMember, MemoryPackOrder(2)] public HashString? SourceContentHash { get; init; }
    [DataMember, MemoryPackOrder(3)] public Option<StreamId?> StreamId { get; init; }
}
