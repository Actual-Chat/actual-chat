namespace ActualChat.Media;

public class Medias(IServiceProvider services) : IMedias
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
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

    // [CommandHandler]
    public virtual async Task OnRemoveMedia(Medias_RemoveMedia command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, mediaId) = command;
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return;

        // If Media has UserId set, only that user can delete it
        if (media.UserId == null)
            throw StandardError.Unauthorized("You don't have permission to delete this media.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (media.UserId != account.Id)
            throw StandardError.Unauthorized("You don't have permission to delete this media.");

        var change = new Change<MediaFull> { Remove = true };
        await Commander.Call(new MediaBackend_Change(mediaId, change), cancellationToken).ConfigureAwait(false);
    }
}
