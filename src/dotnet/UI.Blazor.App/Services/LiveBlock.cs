namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// A viewer's live block, including its effective fold after revealing older messages.
/// Open/closed describe the session lifecycle, independently of expansion.
/// </summary>
public abstract record LiveBlock(
    ConversationId ConversationId,
    bool HasAttended,
    long FoldEndLid)
{
    public Range<long> FoldRange => FoldEndLid > ConversationId.StartEntryLid
        ? new(ConversationId.StartEntryLid, FoldEndLid)
        : default;
}

public sealed record OpenLiveBlock(
    ConversationId ConversationId,
    bool HasAttended,
    long FoldEndLid)
    : LiveBlock(ConversationId, HasAttended, FoldEndLid);

public sealed record ClosedLiveBlock(
    ConversationId ConversationId,
    long FoldEndLid,
    long EndLid,
    ConversationId? MaterializedId,
    Conversation? DissolvingConversation = null)
    : LiveBlock(ConversationId, true, FoldEndLid)
{
    public bool IsDissolving => MaterializedId == null;
    public Range<long> HiddenTailRange => IsDissolving ? new(EndLid, long.MaxValue) : default;
}
