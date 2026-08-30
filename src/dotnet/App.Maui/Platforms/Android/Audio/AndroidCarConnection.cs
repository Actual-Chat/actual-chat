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

    private readonly ILogger _log;
    private UpdateReceiver? _receiver;

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
    }

    protected override void Dispose(bool disposing)
    {
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
        return state == ConnectionTypeProjection;
    }

    // Private methods

    private int ReadState()
    {
        try {
            var uri = Uri.Parse(ConnectionUri)!;
            using var cursor = Platform.AppContext.ContentResolver?.Query(
                uri, [StateColumn], null, null, null);
            if (cursor == null || !cursor.MoveToNext())
                return 0;

            var index = cursor.GetColumnIndex(StateColumn);
            return index < 0 ? 0 : cursor.GetInt(index);
        }
        catch (Exception e) {
            // A missing or unreadable provider must never stop a recording.
            _log.LogDebug(e, "Couldn't read the car connection state");
            return 0;
        }
    }

    // Nested types

    private sealed class UpdateReceiver(ICarConnection owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
            => owner.InvalidateProjectionState();
    }
}
