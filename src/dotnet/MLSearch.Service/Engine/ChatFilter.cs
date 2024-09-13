namespace ActualChat.MLSearch.Engine;

/// <summary>
/// This enum must be considered in conjunction with some entity type we want to qeery for.
/// </summary>
public enum InclusionMode
{
    /// <summary>
    /// <see cref="Include"/> is a default value.
    /// It means entities of the type we consider must be included, as well as other entities.
    /// </summary>
    Include,
    /// <summary>
    /// Only entities of the considered entity type must be included.
    /// </summary>
    IncludeStrictly,
    /// <summary>
    /// Entities of the considered entity type must NOT be included.
    /// </summary>
    Exclude,
}

public sealed class ChatFilter: IQueryFilter
{
    // TODO: Think about better API for this filters.
    // We probably should get rid of the InclusionMode as it looks confusing.
    public InclusionMode PublicChatInclusion { get; init; }
    public InclusionMode BotChatInclusion { get; init; }

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyChatFilter(this);
}
