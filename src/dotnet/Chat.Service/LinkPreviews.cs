namespace ActualChat.Chat;

[Obsolete("2023.10: Use ActualChat.Media.LinkPreviews instead")]
internal class LinkPreviews(IServiceProvider services) : ILinkPreviews
{
    private Media.ILinkPreviews Service { get; } = services.GetRequiredService<Media.ILinkPreviews>();

    // [ComputeMethod]
    public virtual async Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
    {
        var preview = await Service.Get(id, cancellationToken).ConfigureAwait(false);
        if (preview is null)
            return null;

        return new () {
            Id = preview.Id,
            Title = preview.Title,
            Description = preview.Description,
            Url = preview.Description,
            PreviewMediaId = preview.PreviewMediaId,
            CreatedAt = preview.CreatedAt,
            ModifiedAt = preview.ModifiedAt,
            PreviewMedia = preview.PreviewMedia,
        };
    }

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled()
        => Service.IsEnabled();
}
