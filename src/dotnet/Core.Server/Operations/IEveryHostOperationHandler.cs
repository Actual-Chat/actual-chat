using ActualLab.CommandR.Operations;

namespace ActualChat.Operations;

// Replay used to run a command's handler on every host; 15.0 sends only the recorded invalidation
// calls, so an effect that isn't an invalidation needs another carrier. This is it - but it reaches
// the other hosts only while the operation is stored, and without events or invalidation calls the
// default StoreMode is None, a row nobody reads.

/// <summary>
/// Runs part of a committed operation's effect on every host that reads its row.
/// </summary>
public interface IEveryHostOperationHandler
{
    // isOrigin is true only on the host the operation ran on, so one handler can serve both the
    // "here only" and the "everywhere" variant of a command.
    Task OnOperationCompleted(Operation operation, bool isOrigin, CancellationToken cancellationToken);
}
