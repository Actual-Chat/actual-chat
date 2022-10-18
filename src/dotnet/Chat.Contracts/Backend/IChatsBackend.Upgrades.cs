namespace ActualChat.Chat;

public partial interface IChatsBackend
{
    [CommandHandler]
    Task FixCorruptedChatReadPositions(FixCorruptedChatReadPositionsCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record FixCorruptedChatReadPositionsCommand(
    ) : ICommand<Unit>, IBackendCommand;
}
