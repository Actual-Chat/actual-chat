namespace ActualChat.Mesh;

public static partial class MeshLocksExt
{
    public static async Task<bool> RunLocked(
        this IMeshLocks meshLocks,
        string key,
        RunLockedOptions options,
        Func<CancellationToken, Task> taskFactory,
        CancellationToken cancellationToken)
    {
        await meshLocks
            .TryRunLocked(key, options, false, taskFactory.ToUnitTaskFactory(), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public static async Task<Option<T>> RunLocked<T>(
        this IMeshLocks meshLocks,
        string key,
        RunLockedOptions options,
        Func<CancellationToken, Task<T>> taskFactory,
        CancellationToken cancellationToken)
    {
        var resultOpt = await meshLocks
            .TryRunLocked(key, options, false, taskFactory, cancellationToken)
            .ConfigureAwait(false);
        return resultOpt.Value;
    }

    public static async Task<bool> TryRunLocked(
        this IMeshLocks meshLocks,
        string key,
        RunLockedOptions options,
        Func<CancellationToken, Task> taskFactory,
        CancellationToken cancellationToken)
    {
        var resultOpt = await meshLocks
            .TryRunLocked(key, options, true, taskFactory.ToUnitTaskFactory(), cancellationToken)
            .ConfigureAwait(false);
        return resultOpt.IsSome(out _);
    }

    public static Task<Option<T>> TryRunLocked<T>(
        this IMeshLocks meshLocks,
        string key,
        RunLockedOptions options,
        Func<CancellationToken, Task<T>> taskFactory,
        CancellationToken cancellationToken)
        => meshLocks.TryRunLocked(key, options, true, taskFactory, cancellationToken);

    // Private methods

    private static async Task<Option<T>> TryRunLocked<T>(
        this IMeshLocks meshLocks,
        string key,
        RunLockedOptions options,
        bool exitIfLocked,
        Func<CancellationToken, Task<T>> taskFactory,
        CancellationToken cancellationToken)
    {
        var fullKey = (string?)null;
        var method = exitIfLocked ? nameof(TryRunLocked) : nameof(RunLocked);
        var log = options.Log;
        var tryIndex = 0;
        while (true) {
            try {
                MeshLockHolder? h;
                if (exitIfLocked) {
                    h = await meshLocks.TryLock(key, cancellationToken).ConfigureAwait(false);
                    if (h == null)
                        return default;
                }
                else
                    h = await meshLocks.Lock(key, cancellationToken).ConfigureAwait(false);

                await using var _ = h.ConfigureAwait(false);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, h.StopToken);
                var result = await taskFactory.Invoke(cts.Token).ConfigureAwait(false);
                return Option<T>.Some(result);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                fullKey ??= meshLocks.GetFullKey(key);
                if (++tryIndex >= options.TryCount) {
                    log?.LogError(e,
                        "MeshLocks.{Method}('{Key}') failed ({TryIndex}/{TryCount}), retry limit is reached",
                        method, fullKey, tryIndex, options.TryCount);
                    throw;
                }

                var delay = options.Delays[tryIndex];
                log?.LogWarning(e,
                    "MeshLocks.{Method}('{Key}') failed ({TryIndex}/{TryCount}), will retry in {Delay}",
                    method, fullKey, tryIndex, options.TryCount, delay.ToShortString());
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
