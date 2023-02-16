namespace ActualChat.UI.Blazor.Services;

public sealed class HistoryChangeTracker : IDisposable
{
    private readonly HistoryUI _historyUI;
    private readonly Action<HistoryItem>? _onChange;
    private readonly EventHandler<LocationChangedEventArgs> _onLocationChanged;

    public HistoryChangeTracker(HistoryUI historyUI, Action<HistoryItem> onChange)
    {
        _historyUI = historyUI;
        _onChange = onChange;
        _onLocationChanged = OnLocationChanged;
    }

    public void Dispose()
        => _historyUI.LocationChanged -= _onLocationChanged;

    public HistoryChangeTracker Start()
    {
        _historyUI.LocationChanged += _onLocationChanged;
        return this;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var item = _historyUI.CurrentItem;
        _onChange?.Invoke(item);
    }
}
