using Markdig.Helpers;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

public record Chat
{
    public Symbol Id { get; init; } = "";
    public long Version { get; init; }
    public string Title { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public bool IsPublic { get; init; }
    public ChatType ChatType { get; init; } = ChatType.Group;
    public ImmutableArray<Symbol> OwnerIds { get; init; } = ImmutableArray<Symbol>.Empty;

    public static bool IsValidId(string chatId)
        => chatId.Length > 0 && chatId.All(c => c.IsAlphaNumeric() || c == '-');
}
