namespace ActualChat.Media;

public interface IMediaBackend : IComputeService
{
    // Commands

    [CommandHandler]
    Task<Media> CreateMedia(CreateMediaCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateMediaCommand(
        [property: DataMember]
        Media Media
    ) : ICommand<Media>, IBackendCommand;
}
