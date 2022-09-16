namespace ActualChat.Chat;

public interface IMentionsBackend
{
    [ComputeMethod]
    Task<Mention?> GetLast(
        Symbol chatId,
        Symbol authorId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand([property: DataMember] ChatEntry Entry) : ICommand<Unit>, IBackendCommand;
}
