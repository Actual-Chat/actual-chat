namespace ActualChat.Users;

public interface IAvatarsBackend : IComputeService
{
    [ComputeMethod]
    Task<AvatarFull?> Get(string avatarId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AvatarFull> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Symbol AvatarId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<AvatarFull> Change
        ) : ICommand<AvatarFull>, IBackendCommand;
}
