namespace ActualChat.Users;

public interface IReadPositionsBackend : IComputeService
{
    [ComputeMethod]
    Task<long?> Get(string userId, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task Set(SetCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetCommand(
        [property: DataMember] string UserId,
        [property: DataMember] string ChatId,
        [property: DataMember] long ReadEntryId,
        [property: DataMember] bool Force = false
        ) : ICommand<Unit>, IBackendCommand;
}
