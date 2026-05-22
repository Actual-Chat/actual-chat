using ActualChat.Search;

namespace ActualChat.Chat;

public sealed class FoundMention(MentionId id, SearchMatch match, Picture picture) : IHasSearchMatch
{
    public MentionId MentionId { get; } = id;
    public SearchMatch Match { get; } = match;
    public string Text => Match.Text;
    public Picture Picture { get; } = picture;
    public bool IsChatMember { get; init; }
    public string? Description { get; init; } // "emoji", "place", "in this place", null = no suffix
    public string MentionName { get; init; } = ""; // "Place › Chat" for a cross-place chat, otherwise plain title
}
