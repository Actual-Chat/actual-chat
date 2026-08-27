namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Warms whatever a <see cref="PrefetchRef"/> points at. Implementations are resolved from DI by
/// type and must be cheap to call repeatedly: the same target is prefetched again on every pointer
/// down, and deduplication is left to the compute methods they touch.
/// </summary>
public interface IPrefetcher
{
    Task Prefetch(string[] arguments, CancellationToken cancellationToken);
}
