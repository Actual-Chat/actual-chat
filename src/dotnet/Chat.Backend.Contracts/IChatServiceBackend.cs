namespace ActualChat.Chat;

/// <summary>
/// The facade which is used to access <see cref="ActualChat.Chat.IChatService"/> from an external services.
/// </summary>
public interface IChatServiceBackend
{
    Task<ChatEntry> CreateEntry(ChatEntry Entry, CancellationToken cancellationToken);
    Task<ChatEntry> UpdateEntry(ChatEntry Entry, CancellationToken cancellationToken);
}
