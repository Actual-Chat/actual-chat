namespace ActualChat.UI.Blazor.Services;

public interface IBackgroundActivityProvider: IComputeService
{
    [ComputeMethod]
    Task<bool> GetIsActive(CancellationToken cancellationToken);
}
