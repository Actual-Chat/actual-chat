namespace ActualChat.Chat;

internal class ChatServiceBackend : IChatServiceBackend
{
    private readonly ICommander _commander;

    public ChatServiceBackend(ICommander commander) => _commander = commander;

    public async Task<ChatEntry> CreateEntry(ChatEntry entry, CancellationToken cancellationToken)
        => await _commander.Call(new IChatService.CreateEntryCommand(entry), isolate: true, cancellationToken)
            .ConfigureAwait(false);

    public async Task<ChatEntry> UpdateEntry(ChatEntry entry, CancellationToken cancellationToken)
        => await _commander.Call(new IChatService.UpdateEntryCommand(entry), isolate: true, cancellationToken)
            .ConfigureAwait(false);
}