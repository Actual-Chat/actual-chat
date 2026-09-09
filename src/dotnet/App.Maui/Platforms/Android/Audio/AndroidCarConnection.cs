using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using Android.Database;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Reads the Android Auto connection state from the gearhead content provider -
/// the same source <c>androidx.car.app.connection.CarConnection</c> reads, without the dependency.
/// </summary>
public class AndroidCarConnection : SafeDisposableBase, ICarConnection
{
    private const string ConnectionUri = "content://androidx.car.app.connection";
    private const string StateColumn = "CarConnectionState";
    private const string UpdateAction = "androidx.car.app.connection.action.CAR_CONNECTION_UPDATED";
    private const int ConnectionTypeProjection = 2;
    private static readonly TimeSpan RecheckPeriod = TimeSpan.FromSeconds(30);

    private readonly ILogger _log;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private UpdateReceiver? _receiver;
    private int _lastKnownState;

    public AndroidCarConnection(ILogger<AndroidCarConnection> log)
    {
        _log = log;
        try {
            var receiver = new UpdateReceiver(this);
            var filter = new IntentFilter(UpdateAction);
            // CAR_CONNECTION_UPDATED is sent by the gearhead package, i.e. from another UID, and on
            // API 33+ NotExported accepts same-UID and system broadcasts only. androidx.car.app's
            // CarConnectionTypeLiveData registers this very filter with RECEIVER_EXPORTED.
            Platform.AppContext.RegisterReceiver(receiver, filter, ReceiverFlags.Exported);
            _receiver = receiver;
        }
        catch (Exception e) {
            // Degrades to "read but never invalidated": the state still answers, it just stops
            // tracking connects. A detector that can't register must not break recording.
            _log.LogWarning(e, "Couldn't register the car connection update receiver");
        }
        // The broadcast is the primary signal; the recheck catches one that never arrived.
        _ = AsyncChain.From(Recheck)
            .RetryForever(RetryDelaySeq.Exp(3, 60), _log)
            .CycleForever()
            .RunIsolated(_disposeTokenSource.Token);
    }

    protected override void Dispose(bool disposing)
    {
        _disposeTokenSource.Cancel();
        var receiver = _receiver;
        if (receiver == null)
            return;

        _receiver = null;
        try {
            Platform.AppContext.UnregisterReceiver(receiver);
        }
        catch { /* Ignore */ }
        receiver.Dispose();
    }

    [ComputeMethod]
    public virtual async Task<bool> IsProjectionActive(CancellationToken cancellationToken)
    {
        // ReadState blocks on a cross-process content provider call, and the callers that matter
        // most - audio focus renewal, recording and playback start - are on threads where that costs.
        var state = await Task.Run(ReadState, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _lastKnownState, state); // Read by the recheck chain on the thread pool
        var isProjectionActive = state == ConnectionTypeProjection;
        _log.LogInformation("IsProjectionActive: provider state {State} -> {IsProjectionActive}",
            state, isProjectionActive);
        return isProjectionActive;
    }

    public void InvalidateProjectionState()
    {
        _log.LogInformation("InvalidateProjectionState");
        using (Invalidation.Begin())
            _ = IsProjectionActive(default);
    }

    // Private methods

    private int ReadState()
    {
        // A failed read answers with the last known state: a transient provider hiccup must
        // not turn "projecting" into "not projecting", which is what opens a call to the car.
        try {
            var uri = Uri.Parse(ConnectionUri)!;
            using var cursor = Platform.AppContext.ContentResolver?.Query(
                uri, [StateColumn], null, null, null);
            if (cursor == null || !cursor.MoveToNext()) {
                var lastKnownState = Volatile.Read(ref _lastKnownState);
                _log.LogWarning("Car connection provider returned {Result}, keeping state {State}",
                    cursor == null ? "null" : "no rows", lastKnownState);
                return lastKnownState;
            }

            var index = cursor.GetColumnIndex(StateColumn);
            return index < 0 ? 0 : cursor.GetInt(index);
        }
        catch (Exception e) {
            var lastKnownState = Volatile.Read(ref _lastKnownState);
            _log.LogWarning(e, "Couldn't read the car connection state, keeping state {State}", lastKnownState);
            return lastKnownState;
        }
    }

    private async Task Recheck(CancellationToken cancellationToken)
    {
        await Task.Delay(RecheckPeriod, cancellationToken).ConfigureAwait(false);
        var state = ReadState();
        var lastKnownState = Volatile.Read(ref _lastKnownState);
        if (state == lastKnownState)
            return;

        _log.LogInformation("Car connection recheck: state {Old} -> {New}", lastKnownState, state);
        InvalidateProjectionState();
    }

    // Nested types

    private sealed class UpdateReceiver(AndroidCarConnection owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            owner._log.LogInformation("Car connection broadcast: {Action}", intent?.Action);
            owner.InvalidateProjectionState();
        }
    }
}
