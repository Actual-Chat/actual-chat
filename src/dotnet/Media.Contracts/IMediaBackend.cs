namespace ActualChat.Media;

public interface IMediaBackend : IComputeService
{
    [ComputeMethod]
    public Task<Media?> Get(MediaId mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<Media?> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] MediaId Id,
        [property: DataMember] Change<Media> Change
    ) : ICommand<Media?>, IBackendCommand;
}
