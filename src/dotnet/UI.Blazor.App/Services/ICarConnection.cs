namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Reports whether the device is projecting into a car head unit right now.
/// Registered per platform; absent where projection isn't a thing.
/// </summary>
public interface ICarConnection : IComputeService
{
    [ComputeMethod]
    Task<bool> IsProjectionActive(CancellationToken cancellationToken);

    // A default implementation, not an abstract member: the generated compute-service proxy
    // implements [ComputeMethod] members only, and an unimplemented one wouldn't compile.
    void InvalidateProjectionState()
    {
        using (Invalidation.Begin())
            _ = IsProjectionActive(default);
    }
}
