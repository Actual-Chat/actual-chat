namespace ActualChat.Users;

/// <summary>
/// Chat-scoped KVAS backed by <see cref="IServerKvasBackend"/>: shared per-chat storage
/// (visible to everyone), not per-user. Reuses the existing KvasEntries table - no migration.
/// </summary>
public sealed class ChatScopedKvasBackend(
    IServerKvasBackend serverKvasBackend,
    ChatId chatId,
    bool isOutermost = false
    ) : ServerKvasBackendClient(serverKvasBackend, GetChatPrefix(chatId.Require()), isOutermost)
{
    public ChatId ChatId { get; } = chatId.Require();

    public static string GetChatPrefix(ChatId chatId) => $"c/{chatId.Value}/";
}
