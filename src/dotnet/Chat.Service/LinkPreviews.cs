namespace ActualChat.Chat;

[Obsolete("2023.10: Use ActualChat.Media.LinkPreviews instead")]
public class LinkPreviews(IServiceProvider services) : ILinkPreviews
{
    private Media.IMediaLinkPreviews Service { get; } = services.GetRequiredService<Media.IMediaLinkPreviews>();

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled()
        => ActualLab.Async.TaskExt.TrueTask;

    // [ComputeMethod]
    public virtual async Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
    {
        var preview = await Service.Get(id, cancellationToken).ConfigureAwait(false);
        if (preview is null)
            return null;

        return new () {
            Id = preview.Id,
            Url = preview.Description,
            PreviewMediaId = preview.PreviewMediaId,
            Title = preview.Title,
            Description = preview.Description,
            CreatedAt = preview.CreatedAt,
            ModifiedAt = preview.ModifiedAt,
            PreviewMedia = preview.PreviewMedia,
        };
    }
}
