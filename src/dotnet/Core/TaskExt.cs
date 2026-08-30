namespace ActualChat;

public static class TaskExt
{
    // NeverEnding

    // Same as ActualLab.Async.TaskExt.NeverEnding(cancellationToken);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task NeverEnding(CancellationToken cancellationToken)
        => Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);

    // TryWaitAsync

    // False means the wait was cut short rather than the task completing, so the caller decides
    // what that was - both current callers re-check their own token right after.
    public static async Task<bool> TryWaitAsync(this Task task, CancellationToken cancellationToken)
    {
        try {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) {
            return false;
        }
    }

    public static async Task<bool> TryWaitAsync(
        this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) {
            return false;
        }
        catch (TimeoutException) {
            return false;
        }
    }

    // WithDelay

    public static async Task WithDelay(
        this Task task, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        await task.ConfigureAwait(false);
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> WithDelay<T>(
        this Task<T> task, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var result = await task.ConfigureAwait(false);
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // WithErrorHandler

    public static async Task WithErrorHandler(
        this Task task, Action<Exception> errorHandler, CancellationToken cancellationToken = default)
    {
        try {
            await task.ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            errorHandler.Invoke(e);
            throw;
        }
    }

    public static async Task<T> WithErrorHandler<T>(
        this Task<T> task, Action<Exception> errorHandler, CancellationToken cancellationToken = default)
    {
        try {
            return await task.ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            errorHandler.Invoke(e);
            throw;
        }
    }

    public static async ValueTask WithErrorHandler(
        this ValueTask task, Action<Exception> errorHandler, CancellationToken cancellationToken = default)
    {
        try {
            await task.ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            errorHandler.Invoke(e);
            throw;
        }
    }

    public static async ValueTask<T> WithErrorHandler<T>(
        this ValueTask<T> task, Action<Exception> errorHandler, CancellationToken cancellationToken = default)
    {
        try {
            return await task.ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            errorHandler.Invoke(e);
            throw;
        }
    }

    // WithErrorLog

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task WithErrorLog(this Task task, ILogger? errorLog, string message, params object?[] args)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => errorLog is null ? task
            : task.WithErrorHandler(e => errorLog.LogError(e, message, args));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> WithErrorLog<T>(this Task<T> task, ILogger? errorLog, string message, params object?[] args)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => errorLog is null ? task
            : task.WithErrorHandler(e => errorLog.LogError(e, message, args));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask WithErrorLog(this ValueTask task, ILogger? errorLog, string message, params object?[] args)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => errorLog is null ? task
            : task.WithErrorHandler(e => errorLog.LogError(e, message, args));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<T> WithErrorLog<T>(this ValueTask<T> task, ILogger? errorLog, string message, params object?[] args)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => errorLog is null ? task
            : task.WithErrorHandler(e => errorLog.LogError(e, message, args));


    // WithErrorLog with CancellationToken overloads

#pragma warning disable CA1068 // CancellationToken parameters must come last
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task WithErrorLog(
            this Task task, CancellationToken cancellationToken,
            ILogger? errorLog, string message, params object?[] args)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => errorLog is null ? task
            : task.WithErrorHandler(e => errorLog.LogError(e, message, args), cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> WithErrorLog<T>(
            this Task<T> task, CancellationToken cancellationToken,
            ILogger? errorLog, string message, params object?[] args)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => errorLog is null ? task
            : task.WithErrorHandler(e => errorLog.LogError(e, message, args), cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask WithErrorLog(
            this ValueTask task, CancellationToken cancellationToken,
            ILogger? errorLog, string message, params object?[] args)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => errorLog is null ? task
            : task.WithErrorHandler(e => errorLog.LogError(e, message, args), cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<T> WithErrorLog<T>(
            this ValueTask<T> task, CancellationToken cancellationToken,
            ILogger? errorLog, string message, params object?[] args)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => errorLog is null ? task
            : task.WithErrorHandler(e => errorLog.LogError(e, message, args), cancellationToken);
#pragma warning restore CA1068 // CancellationToken parameters must come last

    // WhenAll

    public static async ValueTask WhenAll(ValueTask task1, ValueTask task2)
    {
        if (task1.IsCompletedSuccessfully && task2.IsCompletedSuccessfully)
            return;

        if (task1.IsCompletedSuccessfully)
            await task2.ConfigureAwait(false);

        if (task2.IsCompletedSuccessfully)
            await task1.ConfigureAwait(false);

        Exception? exception1 = null;
        Exception? exception2 = null;
        try {
            await task1.ConfigureAwait(false);
        }
        catch (Exception e) {
            exception1 = e;
        }

        if (exception1 == null) {
            await task2.ConfigureAwait(false);
            return;
        }

        try {
            await task2.ConfigureAwait(false);
        }
        catch (Exception e) {
            exception2 = e;
        }
        if (exception2 == null)
            throw exception1;

        throw new AggregateException(exception1, exception2);
    }

    public static async ValueTask WhenAll(this IEnumerable<ValueTask> source)
    {
        List<Exception>? exceptions = null;

        foreach (var valueTask in source)
            try {
                if (valueTask.IsCompletedSuccessfully)
                    continue;

                await valueTask.ConfigureAwait(false);
            }
            catch (Exception ex) {
                exceptions ??= [];
                exceptions.Add(ex);
            }

        if (exceptions is not null)
            throw exceptions.Count switch {
                1 => exceptions[0],
                _ => new AggregateException(exceptions),
            };
    }

    // WhenAny

    // Source (with some refactorings):
    // - https://github.com/dotnet/reactive/blob/93386a90d9e7a78c2a0c3aaa16d31e1328f71b72/Ix.NET/Source/System.Interactive.Async/TaskExt.cs#L16
    public static WhenAnyValueTask<T> WhenAny<T>(ValueTask<T>[] tasks)
    {
        var whenAny = new WhenAnyValueTask<T>(tasks);
        whenAny.Start();
        return whenAny;
    }

    // Nested types

    public sealed class WhenAnyValueTask<T>
    {
        /// <summary>
        /// The tasks to await. Entries in this array may be replaced using <see cref="Replace"/>.
        /// </summary>
        private ValueTask<T>[] _tasks;

        /// <summary>
        /// Array of cached delegates passed to awaiters on tasks. These delegates have a closure containing the task index.
        /// </summary>
        private readonly Action[] _onReady;

        /// <summary>
        /// Queue of indexes of ready tasks. Awaiting the <see cref="WhenAnyValueTask{T}"/> object will consume this queue in order.
        /// </summary>
        /// <remarks>
        /// A lock on this field is taken when updating the queue or <see cref="_onCompleted"/>.
        /// </remarks>
        private readonly Queue<int> _ready;

        /// <summary>
        /// Callback of the current awaiter, if any.
        /// </summary>
        /// <remarks>
        /// Protected for reads and writes by a lock on <see cref="_ready"/>.
        /// </remarks>
        private Action? _onCompleted;

        /// <summary>
        /// Creates a when any task around the specified tasks.
        /// </summary>
        /// <param name="tasks">Initial set of tasks to await.</param>
        public WhenAnyValueTask(ValueTask<T>[] tasks)
        {
            _tasks = tasks;
            var n = tasks.Length;
            _ready = new Queue<int>(n); // NB: Should never exceed this length, so we won't see dynamic realloc.
            _onReady = new Action[n];
            for (var i = 0; i < n; i++) {
                //
                // Cache these delegates, for they have closures (over `this` and `index`), and we need them
                // for each replacement of a task, to hook up the continuation.
                //
                int index = i;
                _onReady[index] = () => OnReady(index);
            }
        }

        /// <summary>
        /// Start awaiting the tasks. This is done separately from the constructor to avoid complexity dealing
        /// with handling concurrent callbacks to the current instance while the constructor is running.
        /// </summary>
        public void Start()
        {
            for (var i = 0; i < _tasks.Length; i++) {
                //
                // Register a callback with the task, which will enqueue the index of the completed task
                // for consumption by awaiters.
                //
                _tasks[i].ConfigureAwait(false).GetAwaiter().OnCompleted(_onReady[i]);
            }
        }

        /// <summary>
        /// Gets an awaiter to await completion of any of the awaited tasks, returning the index of the completed
        /// task. When sequentially awaiting the current instance, task indices are yielded in the order that of
        /// completion. If all tasks have completed and been observed by awaiting the current instance, the awaiter
        /// never returns on a subsequent attempt to await the completion of any task. The caller is responsible
        /// for bookkeeping that avoids awaiting this instance more often than the number of pending tasks.
        /// </summary>
        /// <returns>Awaiter to await completion of any of the awaited task.</returns>
        /// <remarks>This class only supports a single active awaiter at any point in time.</remarks>
        public Awaiter GetAwaiter() => new Awaiter(this);

        /// <summary>
        /// Replaces the (completed) task at the specified <paramref name="index"/> and starts awaiting it.
        /// </summary>
        /// <param name="index">The index of the parameter to replace.</param>
        /// <param name="task">The new task to store and await at the specified index.</param>
        public void Replace(int index, in ValueTask<T> task)
        {
            _tasks[index] = task;
            task.ConfigureAwait(false).GetAwaiter().OnCompleted(_onReady[index]);
        }

        /// <summary>
        /// Replaces the task buffer and starts awaiting new entries. Previous task buffer must be copied at the beginning of the new one.
        /// </summary>
        /// <param name="tasks">The new task buffer to store and await.</param>
        public void Replace(ValueTask<T>[] tasks)
        {
            if (_tasks.Length >= tasks.Length)
                throw StandardError.Constraint(
                    "New tasks buffer should be larger than existing one. "
                    + "Ensure it contains all entries from the old one at the beginning of the buffer.");

            var oldLength = _tasks.Length;
            _tasks = tasks;
            for (var i = oldLength; i < _tasks.Length; i++) {
                //
                // Register a callback with the task, which will enqueue the index of the completed task
                // for consumption by awaiters.
                //
                _tasks[i].ConfigureAwait(false).GetAwaiter().OnCompleted(_onReady[i]);
            }
        }

        /// <summary>
        /// Called when any task has completed (thus may run concurrently).
        /// </summary>
        /// <param name="index">The index of the completed task in <see cref="_tasks"/>.</param>
        private void OnReady(int index)
        {
            Action? onCompleted = null;

            lock (_ready) {
                //
                // Store the index of the task that has completed. This will be picked up from GetResult.
                //
                _ready.Enqueue(index);

                //
                // If there's a current awaiter, we'll steal its continuation action and invoke it. By setting
                // the continuation action to null, we avoid waking up the same awaiter more than once. Any
                // task completions that occur while no awaiter is active will end up being enqueued in _ready.
                //
                if (_onCompleted != null) {
                    onCompleted = _onCompleted;
                    _onCompleted = null;
                }
            }

            onCompleted?.Invoke();
        }

        /// <summary>
        /// Invoked by awaiters to check if any task has completed, in order to short-circuit the await operation.
        /// </summary>
        /// <returns><c>true</c> if any task has completed; otherwise, <c>false</c>.</returns>
        private bool IsCompleted()
        {
            // REVIEW: Evaluate options to reduce locking, so the single consuming awaiter has limited contention
            //         with the multiple concurrent completing enumerator tasks, e.g. using ConcurrentQueue<T>.
            lock (_ready)
                return _ready.Count > 0;
        }

        /// <summary>
        /// Gets the index of the earliest task that has completed, used by the awaiter. After stealing an index from
        /// the ready queue (by means of awaiting the current instance), the user may chose to replace the task at the
        /// returned index by a new task, using the <see cref="Replace"/> method.
        /// </summary>
        /// <returns>Index of the earliest task that has completed.</returns>
        private int GetResult()
        {
            lock (_ready)
                return _ready.Dequeue();
        }

        /// <summary>
        /// Register a continuation passed by an awaiter.
        /// </summary>
        /// <param name="action">The continuation action delegate to call when any task is ready.</param>
        private void OnCompleted(Action action)
        {
            bool shouldInvoke = false;
            lock (_ready) {
                //
                // Check if we have anything ready (which could happen in the short window between checking
                // for IsCompleted and calling OnCompleted). If so, we should invoke the action directly. Not
                // doing so would be a correctness issue where a task has completed, its index was enqueued,
                // but the continuation was never called (unless another task completes and calls the action
                // delegate, whose subsequent call to GetResult would pick up the lost index).
                //

                if (_ready.Count > 0) {
                    shouldInvoke = true;
                }
                else {
                    Debug.Assert(_onCompleted == null, "Only a single awaiter is allowed.");

                    _onCompleted = action;
                }
            }

            //
            // NB: We assume this case is rare enough (IsCompleted and OnCompleted happen right after one
            //     another, and an enqueue should have happened right in between to go from an empty to a
            //     non-empty queue), so we don't run the risk of triggering a stack overflow due to
            //     synchronous completion of the await operation (which may be in a loop that awaits the
            //     current instance again).
            //
            if (shouldInvoke)
                action();
        }

        /// <summary>
        /// Awaiter type used to await completion of any task.
        /// </summary>
        public readonly struct Awaiter(WhenAnyValueTask<T> parent) : INotifyCompletion
        {
            public bool IsCompleted => parent.IsCompleted();
            public int GetResult() => parent.GetResult();
            public void OnCompleted(Action continuation) => parent.OnCompleted(continuation);
        }
    }
}
