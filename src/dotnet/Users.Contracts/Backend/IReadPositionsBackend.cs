namespace ActualChat.Users;

public interface IReadPositionsBackend : IComputeService
{
    [ComputeMethod]
    Task<long?> Get(UserId userId, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task Set(SetCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetCommand(
        [property: DataMember] UserId UserId,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] long ReadEntryId,
        [property: DataMember] bool Force = false
        ) : ICommand<Unit>, IBackendCommand;
}
