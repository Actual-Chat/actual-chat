namespace ActualChat.Users;

public interface IAvatarsBackend : IComputeService
{
    [ComputeMethod]
    Task<AvatarFull?> Get(string avatarId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AvatarFull> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] string AvatarId,
        [property: DataMember] Change<AvatarFull> Change
        ) : ICommand<AvatarFull>, IBackendCommand;
}
