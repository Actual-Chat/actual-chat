namespace Content.Contracts;

public interface IContentSaverBackend
{
    [CommandHandler]
    Task SaveContent(SaveContentCommand command, CancellationToken cancellationToken);

    public record SaveContentCommand(string ContentId, byte[] Content, string ContentType) : ICommand<Unit>,
        IBackendCommand {}
}
