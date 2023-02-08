using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public sealed class HistoryTransaction : IHasServices, IHistoryTransactionImpl
{
    private readonly object _lock = new();
    private volatile HistoryChange _change;
    private volatile bool _isCancelled;
    private volatile bool _isClosed;

    IServiceProvider IHasServices.Services => Hub;
    public HistoryHub Hub { get; }
    public HistoryChange Change => _change;
    public bool IsCancelled => _isCancelled;
    public bool IsClosed => _isClosed;

    public HistoryTransaction(HistoryChange change)
    {
        Hub = change.Hub;
        _change = change;
    }

    bool IHistoryTransactionImpl.TryClose()
    {
        lock (_lock) {
            if (_isClosed)
                return false;

            _isClosed  = true;
            return !_isCancelled;
        }
    }

    public bool Cancel()
    {
        lock (_lock) {
            if (_isClosed)
                return false;

            _isClosed = true;
            _isCancelled = true;
            return true;
        }
    }

    public void Update(Func<HistoryChange, HistoryItem> updater)
    {
        lock (_lock) {
            ThrowIfClosed();
            try {
                var item = updater.Invoke(_change);
                _change = _change.With(item);
            }
            catch (Exception) {
                _isCancelled = true;
                throw;
            }
        }
    }

    public void Update<TState>(Func<HistoryChange, TState> updater)
        where TState : HistoryState
    {
        lock (_lock) {
            ThrowIfClosed();
            try {
                var state = updater.Invoke(_change);
                _change = _change.With(state);
            }
            catch (Exception) {
                _isCancelled = true;
                throw;
            }
        }
    }

    public void Update<TState>(Func<HistoryChange, TState, TState> updater)
        where TState : HistoryState
    {
        lock (_lock) {
            ThrowIfClosed();
            try {
                var state = updater.Invoke(_change, _change.Item.GetState<TState>());
                _change = _change.With(state);
            }
            catch (Exception) {
                _isCancelled = true;
                throw;
            }
        }
    }

    // Private methods

    private void ThrowIfClosed()
    {
        if (_isClosed)
            throw StandardError.StateTransition("History transaction is already closed.");
    }
}
