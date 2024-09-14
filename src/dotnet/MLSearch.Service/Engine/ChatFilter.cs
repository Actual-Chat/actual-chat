// namespace ActualChat.MLSearch.Engine;

// /// <summary>
// /// This enum must be considered in conjunction with some entity type we want to qeery for.
// /// </summary>
// public enum InclusionMode
// {
//     /// <summary>
//     /// <see cref="Include"/> is a default value.
//     /// It means entities of the type we consider must be included, as well as other entities.
//     /// </summary>
//     Include,
//     /// <summary>
//     /// Only entities of the considered entity type must be included.
//     /// </summary>
//     IncludeStrictly,
//     /// <summary>
//     /// Entities of the considered entity type must NOT be included.
//     /// </summary>
//     Exclude,
// }

// public sealed class ChatFilter: IQueryFilter
// {
//     // TODO: Think about better API for this filters.
//     // We probably should get rid of the InclusionMode as it looks confusing.
//     public InclusionMode PublicChatInclusion { get; init; }
//     public InclusionMode SearchBotChatInclusion { get; init; }

//     public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyChatFilter(this);
// }

using ActualChat.MLSearch.Engine;

public sealed class ChatFilter: IQueryFilter
{
    public bool IncludePublic { get; set; }
    public bool IncludeSearch { get; set; }

    public ISet<PlaceId> PlaceIds { get;} = new HashSet<PlaceId>();
    public ISet<ChatId> ChatIds { get;} = new HashSet<ChatId>();

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyChatFilter(this);
}
