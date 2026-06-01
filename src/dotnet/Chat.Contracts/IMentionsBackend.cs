using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for tracking mentions in chat entries.
/// </summary>
public interface IMentionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Mention?> GetLast(
        ChatId chatId,
        MentionRef mentionId,
        CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}
