using System.Text;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Search;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ContactIndexState(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [property: DataMember, MemoryPackOrder(2)] public Symbol LastUpdatedId { get; init; }
    [property: DataMember, MemoryPackOrder(3)] public long LastUpdatedVersion { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId LastUpdatedChatId => new (LastUpdatedId, ParseOrNone.Option);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId LastUpdatedUserId => new (LastUpdatedId, ParseOrNone.Option);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public AuthorId LastUpdatedPlaceAuthorId => new (LastUpdatedId, ParseOrNone.Option);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"#{Id}, LastUpdatedId: {LastUpdatedId}, LastUpdatedVersion: {LastUpdatedVersion}");
        return true;
    }
}
