using System.Diagnostics;
using ActualLab.IO;

namespace ActualChat.Concurrency;

public enum SingleInstanceOutcome
{
    NotStarted = 0,
    NoOtherInstance,
    OtherInstanceExited,
    OtherInstanceEnded,
    OtherInstanceRemained,
    Failed,
}

/// <summary>
/// Keeps one process per name, system-wide. A newer process asks the holder to leave through a named event
/// and the holder leaves by itself; it's ended from outside only when it never answers. Windows only:
/// named events are not supported on Unix. The ownership lives on a thread of its own,
/// because a mutex belongs to the thread that took it.
/// <see cref="ExitRequested"/> is raised on that thread, and the name is released once it returns.
/// </summary>
public class SingleInstanceGuard(string name, FilePath pidFilePath) : IDisposable
{
    private readonly ManualResetEvent _stopEvent = new(false);
    private Thread? _holderThread;
    private int _isStarted;
    private int _isDisposed;

    public string Name { get; } = name;
    public FilePath PidFilePath { get; } = pidFilePath;
    public TimeSpan HandOverTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan EndTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public SingleInstanceOutcome Outcome { get; private set; }
    public Exception? Error { get; private set; }

    public event Action? ExitRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        _stopEvent.Set();
        _holderThread?.Join();
        _stopEvent.Dispose();
    }

    public Task<bool> TakeOver()
    {
        // False when another instance still holds the name after everything has been tried;
        // takes up to HandOverTimeout + EndTimeout, and blocks only the holder thread
        if (Interlocked.Exchange(ref _isStarted, 1) != 0)
            throw new InvalidOperationException($"{GetType().Name}.{nameof(TakeOver)} can be called only once.");

        var whenTaken = TaskCompletionSourceExt.New<bool>();
        _holderThread = new Thread(() => Hold(whenTaken)) {
            IsBackground = true,
            Name = GetType().Name,
        };
        _holderThread.Start();
        return whenTaken.Task;
    }

    // Protected/internal methods

    protected virtual bool TryEndHolder()
    {
        try {
            if (!int.TryParse(File.ReadAllText(PidFilePath), out var processId))
                return false;

            using var process = Process.GetProcessById(processId);
            using var currentProcess = Process.GetCurrentProcess();
            if (process.ProcessName != currentProcess.ProcessName)
                return false;

            process.Kill(true);
            return process.WaitForExit(EndTimeout);
        }
        catch {
            return false;
        }
    }

    protected static string GetMutexName(string name)
        => $"Local\\ActualChat-Instance-{name}";

    protected static string GetReplaceEventName(string name)
        => $"Local\\ActualChat-Replace-{name}";

    // Private methods

    private void Hold(TaskCompletionSource<bool> whenTaken)
    {
        try {
            using var mutex = new Mutex(false, GetMutexName(Name));
            using var replaceEvent = new EventWaitHandle(
                false, EventResetMode.AutoReset, GetReplaceEventName(Name));
            if (!Enter(mutex, replaceEvent)) {
                whenTaken.TrySetResult(false);
                return;
            }

            // A request sent while the previous holder was still going away is not for this instance
            replaceEvent.Reset();
            File.WriteAllText(PidFilePath, Environment.ProcessId.ToString());
            whenTaken.TrySetResult(true);
            if (WaitHandle.WaitAny([replaceEvent, _stopEvent]) == 0)
                ExitRequested?.Invoke();
        }
        catch (Exception e) {
            // A guard that can't work must not keep the process from starting
            Error = e;
            Outcome = SingleInstanceOutcome.Failed;
            whenTaken.TrySetResult(true);
        }
    }

    private bool Enter(Mutex mutex, EventWaitHandle replaceEvent)
    {
        if (TryWait(mutex, TimeSpan.Zero)) {
            Outcome = SingleInstanceOutcome.NoOtherInstance;
            return true;
        }

        replaceEvent.Set();
        if (TryWait(mutex, HandOverTimeout)) {
            Outcome = SingleInstanceOutcome.OtherInstanceExited;
            return true;
        }

        if (TryEndHolder() && TryWait(mutex, EndTimeout)) {
            Outcome = SingleInstanceOutcome.OtherInstanceEnded;
            return true;
        }

        Outcome = SingleInstanceOutcome.OtherInstanceRemained;
        return false;
    }

    private static bool TryWait(Mutex mutex, TimeSpan timeout)
    {
        try {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException) {
            // The previous holder died without releasing it, and this thread owns it now all the same
            return true;
        }
    }
}
