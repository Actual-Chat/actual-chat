namespace ActualChat.Chat;

public interface IContentSaverBackend : IComputeService
{
    [CommandHandler]
    Task SaveContent(SaveContentCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SaveContentCommand(
        [property: DataMember] Symbol ContentId,
        [property: DataMember] byte[] Content,
        [property: DataMember] string ContentType
        ) : ICommand<Unit>, IBackendCommand;
}
