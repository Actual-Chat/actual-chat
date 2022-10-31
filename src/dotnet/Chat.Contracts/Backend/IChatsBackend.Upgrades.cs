namespace ActualChat.Chat;

public partial interface IChatsBackend
{
    [CommandHandler]
    Task FixCorruptedReadPositions(FixCorruptedReadPositionsCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record FixCorruptedReadPositionsCommand(
    ) : ICommand<Unit>, IBackendCommand;
}
