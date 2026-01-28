namespace ActualChat.Media;

public class Medias(IServiceProvider services) : IMedias
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ICommander Commander { get; } = services.Commander();

    // [CommandHandler]
    public virtual async Task<MediaId> OnReserveMedia(Medias_ReserveMedia command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, scope) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);

        var mediaId = MediaId.New(scope);
        var media = new MediaFull(mediaId) { UserId = account.Id };
        var change = new Change<MediaFull> { Create = media };

        await Commander.Call(new MediaBackend_Change(mediaId, change), cancellationToken).ConfigureAwait(false);

        return mediaId;
    }
}
