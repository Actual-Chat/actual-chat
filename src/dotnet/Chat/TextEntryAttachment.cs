using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract]
public sealed record TextEntryAttachment(
    [property: DataMember] Symbol Id,
    [property: DataMember] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public TextEntryId EntryId { get; init; }
    [DataMember] public int Index { get; init; }
    [DataMember] public MediaId MediaId { get; init; }

    // Populated on reads by ChatsBackend
    [DataMember] public Media.Media? Media { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => EntryId.ChatId;

    public TextEntryAttachment() : this(Symbol.Empty) { }
}
