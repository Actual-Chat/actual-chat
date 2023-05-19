using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

public class RegionVisibility : IDisposable
{
    private readonly IMutableState<bool> _isVisible;
    private readonly Action<IState<bool>, StateEventKind> _onDependencyUpdated;

    public IState<bool> IsDocumentVisible { get; }
    public IState<bool> IsRegionVisible { get; }
    public IState<bool> IsVisible => _isVisible;

    public RegionVisibility(IServiceProvider services, IState<bool> isRegionVisible)
    {
        IsRegionVisible = isRegionVisible;
        IsDocumentVisible = services.GetRequiredService<BrowserInfo>().IsVisible;
        _isVisible = services.StateFactory().NewMutable<bool>();
        _onDependencyUpdated = OnDependencyUpdated;
        IsRegionVisible.Updated += _onDependencyUpdated;
        IsDocumentVisible.Updated += _onDependencyUpdated;
        Update();
    }

    public void Dispose()
    {
        IsRegionVisible.Updated -= _onDependencyUpdated;
        IsDocumentVisible.Updated -= _onDependencyUpdated;
    }

    private void Update()
        => _isVisible.Value = IsRegionVisible.Value && IsDocumentVisible.Value;

    private void OnDependencyUpdated(IState<bool> state, StateEventKind eventKind)
        => Update();
}
