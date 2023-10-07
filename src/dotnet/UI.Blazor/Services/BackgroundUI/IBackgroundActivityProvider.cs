namespace ActualChat.UI.Blazor.Services;

public interface IBackgroundActivities : IComputeService
{
    [ComputeMethod]
    Task<bool> IsActiveInBackground(CancellationToken cancellationToken);
}
