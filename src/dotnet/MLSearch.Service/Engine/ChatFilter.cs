namespace ActualChat.MLSearch.Engine;

internal enum InclusionMode
{
    Include,
    IncludeStrictly,
    Exclude,
}

internal sealed class ChatFilter: IQueryFilter
{
    public InclusionMode PublicChatInclusion { get; init; }
    public InclusionMode SearchBotChatInclusion { get; init; }

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyChatFilter(this);
}
