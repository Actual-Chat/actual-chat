namespace ActualChat.Users;

public interface IChatReadPositionsBackend : IComputeService
{
    [ComputeMethod]
    Task<long?> Get(string userId, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task Set(SetReadPositionCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetReadPositionCommand(
        [property: DataMember] string UserId,
        [property: DataMember] string ChatId,
        [property: DataMember] long ReadEntryId
        ) : ICommand<Unit>, IBackendCommand;
}
