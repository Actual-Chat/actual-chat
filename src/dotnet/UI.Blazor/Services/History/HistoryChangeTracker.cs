namespace ActualChat.UI.Blazor.Services;

public sealed class HistoryChangeTracker : IDisposable
{
    private readonly History _history;
    private readonly Action<HistoryItem>? _onChange;
    private readonly EventHandler<LocationChangedEventArgs> _onLocationChanged;

    public HistoryChangeTracker(History history, Action<HistoryItem> onChange)
    {
        _history = history;
        _onChange = onChange;
        _onLocationChanged = OnLocationChanged;
    }

    public void Dispose()
        => _history.LocationChanged -= _onLocationChanged;

    public HistoryChangeTracker Start()
    {
        _history.LocationChanged += _onLocationChanged;
        return this;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var item = _history.CurrentItem;
        _onChange?.Invoke(item);
    }
}
