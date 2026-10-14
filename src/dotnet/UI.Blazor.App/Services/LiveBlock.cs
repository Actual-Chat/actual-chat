namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// A viewer's live block, including its effective fold after revealing older messages.
/// Open/closed describe the session lifecycle, independently of expansion.
/// </summary>
public sealed record LiveBlock(
    ConversationId ConversationId,
    bool HasAttended,
    long FoldEndLid,
    long EndLid = long.MaxValue,
    ConversationId? MaterializedId = null,
    Conversation? DissolvingConversation = null)
{
    public bool IsOpen => EndLid == long.MaxValue;
    public bool IsDissolving => !IsOpen && MaterializedId == null;
    public Range<long> HiddenTailRange => IsDissolving ? new(EndLid, long.MaxValue) : default;
    public Range<long> FoldRange => FoldEndLid > ConversationId.StartEntryLid
        ? new(ConversationId.StartEntryLid, FoldEndLid)
        : default;
}
