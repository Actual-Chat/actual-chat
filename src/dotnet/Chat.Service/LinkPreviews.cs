using ActualChat.Chat.Module;

namespace ActualChat.Chat;

internal class LinkPreviews(IServiceProvider services) : ILinkPreviews
{
    private ILinkPreviewsBackend Backend { get; } = services.GetRequiredService<ILinkPreviewsBackend>();
    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();

    // [ComputeMethod]
    public virtual Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
        => Backend.Get(id, cancellationToken);

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled()
        => Task.FromResult(Settings.EnableLinkPreview);
}
