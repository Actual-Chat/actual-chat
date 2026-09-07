using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

public class RegionVisibility : IDisposable
{
    private readonly IState<bool> _isDocumentVisible;
    private readonly IState<bool>? _isRegionVisible;
    private readonly MutableState<bool> _isVisible;
    private readonly Action<State, StateEventKind>? _onDependencyUpdated;
    private readonly CancellationTokenSource? _disposeTokenSource;

    public IState<bool> IsVisible => _isVisible;

    public RegionVisibility(IServiceProvider services, IState<bool> isRegionVisible)
        : this(services)
    {
        _isRegionVisible = isRegionVisible;
        _onDependencyUpdated = OnDependencyUpdated;
        _isRegionVisible.Updated += _onDependencyUpdated;
        _isDocumentVisible.Updated += _onDependencyUpdated;
        Update();
    }

    public RegionVisibility(
        IServiceProvider services,
        Func<CancellationToken, Task<bool>> computeIsRegionVisible)
        : this(services)
    {
        // A compute method has no Updated event to hang off, so this tracks it the other way round:
        // capture the computed, wait for it to be invalidated, publish the next value right there.
        _disposeTokenSource = new CancellationTokenSource();
        _ = Track(computeIsRegionVisible, _disposeTokenSource.Token);
    }

    private RegionVisibility(IServiceProvider services)
    {
        _isDocumentVisible = services.GetRequiredService<BrowserInfo>().IsVisible;
        _isVisible = services.StateFactory().NewMutable(
            _isDocumentVisible.Value,
            StateCategories.Get(typeof(RegionVisibility), nameof(IsVisible)));
    }

    public void Dispose()
    {
        _disposeTokenSource.CancelAndDisposeSilently();
        if (_onDependencyUpdated is not { } onDependencyUpdated)
            return;

        _isRegionVisible!.Updated -= onDependencyUpdated;
        _isDocumentVisible.Updated -= onDependencyUpdated;
    }

    // Private methods

    private async Task Track(
        Func<CancellationToken, Task<bool>> computeIsRegionVisible,
        CancellationToken cancellationToken)
    {
        var computed = await Computed
            .Capture(() => Compute(computeIsRegionVisible, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in computed.Changes(FixedDelayer.YieldUnsafe, cancellationToken).ConfigureAwait(false)) {
            // The source recomputes for reasons that don't change the answer - a covering panel's
            // deadline, a screen-size tick - and every write here invalidates the whole region.
            if (_isVisible.Value != c.Value)
                _isVisible.Value = c.Value;
        }
    }

    private async Task<bool> Compute(
        Func<CancellationToken, Task<bool>> computeIsRegionVisible,
        CancellationToken cancellationToken)
        => await computeIsRegionVisible(cancellationToken).ConfigureAwait(false)
            && await _isDocumentVisible.Use(cancellationToken).ConfigureAwait(false);

    private void Update()
    {
        var isVisible = _isRegionVisible!.Value && _isDocumentVisible.Value;
        if (_isVisible.Value != isVisible)
            _isVisible.Value = isVisible;
    }

    private void OnDependencyUpdated(State state, StateEventKind eventKind)
        => Update();
}
