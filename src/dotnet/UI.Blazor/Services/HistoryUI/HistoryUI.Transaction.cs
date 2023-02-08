using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI
{
    public const int TransactionRetryLimit = 2;

    private HistoryTransaction? _transaction;

    public HistoryTransaction? Transaction {
        get { lock (Lock) return _transaction; }
    }

    public void Update(Action<HistoryTransaction> updater)
    {
        // We retry here because transaction might be cancelled;
        // currently the only code that cancels them is in HistoryUI.OnLocationChanged -
        // it cancels active transaction in case it happens during the event.
        var retryIndex = 0;
        while (true) {
            using var disposer = BeginTransaction(out var tx);
            if (ReferenceEquals(disposer.Resource, null)) {
                // Not a top-level update call
                updater.Invoke(tx);
                return;
            }

            try {
                updater.Invoke(tx);
                return;
            }
            catch (Exception e) {
                tx.Cancel();
                if (retryIndex++ >= TransactionRetryLimit)
                    throw;
                Log.LogWarning(e, "HistoryTransaction: retry #{RetryIndex}", retryIndex);
            }
        }
    }

    public void Update(Func<HistoryChange, HistoryItem> updater)
        => Update(tx => tx.Update(updater));

    public void Update<TState>(Func<HistoryChange, TState> updater)
        where TState : HistoryState
        => Update(tx => tx.Update(updater));

    public void Update<TState>(Func<HistoryChange, TState, TState> updater)
        where TState : HistoryState
        => Update(tx => tx.Update(updater));

    public void Fix()
        => Update<UriState>((_, state) => state);

    public void NavigateTo(string uri)
        => Update<UriState>((_, state) => state.With(uri));

    // Low-level APIs

    private Disposable<HistoryTransaction> BeginTransaction(out HistoryTransaction tx)
    {
        lock (Lock) {
            if (_transaction != null) {
                // Transaction is already there
                tx = _transaction;
                return default;
            }

            // New transaction
            var change = new HistoryChange(Hub, NewItem(), CurrentItemUnsafe, null, _position, HistoryChangeReason.Update);
            _transaction = tx = new HistoryTransaction(change);
            return new Disposable<HistoryTransaction>(tx, static tx1 => {
                var historyUI = tx1.Hub.HistoryUI;
                lock (historyUI.Lock) {
                    if (historyUI._transaction != tx1)
                        throw StandardError.Internal(
                            "Something is off: HistoryUI transaction differs from the expected one.");
                    try {
                        if (((IHistoryTransactionImpl)tx1).TryClose())
                            historyUI.Apply(tx1.Change);
                    }
                    finally {
                        historyUI._transaction = null;
                    }
                }
            });
        }
    }
}
