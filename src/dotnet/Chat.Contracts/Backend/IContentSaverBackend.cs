namespace ActualChat.Chat;

public interface IContentSaverBackend
{
    [CommandHandler]
    Task SaveContent(SaveContentCommand command, CancellationToken cancellationToken);

    [DataContract]
    public record SaveContentCommand(
        [property: DataMember] string ContentId,
        [property: DataMember] byte[] Content,
        [property: DataMember] string ContentType
        ) : ICommand<Unit>, IBackendCommand;
}
