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
        MentionId mentionId,
        CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}
