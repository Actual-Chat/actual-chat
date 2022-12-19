namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public record struct RelatedChatEntry(
    RelatedEntryKind Kind,
    ChatEntryId Id);

public enum RelatedEntryKind
{
    Reply,
    Edit,
}
