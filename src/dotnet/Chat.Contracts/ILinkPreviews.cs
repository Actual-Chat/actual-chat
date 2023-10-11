namespace ActualChat.Chat;

public interface ILinkPreviews : IComputeService
{
    [ComputeMethod]
    Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> IsEnabled();
}
