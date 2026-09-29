using ActualLab.IO;
using Bunit;
using Bunit.Extensions.WaitForHelpers;
using Microsoft.AspNetCore.Components;

namespace ActualChat.Testing;

/// <summary>
/// The tests' one way to wait for a condition:
/// <see cref="When(Func{CancellationToken, Task}, TimeSpan?, bool, string, int)"/> re-runs its assertion
/// on invalidation, <see cref="WhenPolled(Action, TimeSpan?, bool, string, int)"/> on a timer,
/// <c>WhenRendered</c> after each bUnit render.
/// Every budget passes through here, so the build-agent scale is applied in one place.
/// </summary>
public static class TestWait
{
    private const string ReportFileNamePrefix = "test-waits-";
    private static readonly object ReportLock = new();
    private static readonly TimeSpan DispatcherProbeTimeout = TimeSpan.FromSeconds(5);
    private static StreamWriter? _reportWriter;

    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    // Every budget in the tests is guessed, because nothing ever measured how long a wait really
    // takes under CI load. One line per wait measures it, into a log next to the test binaries that
    // CI uploads as an artifact; what to do with the numbers - a smarter scale, or just honest
    // constants - is decided from them later, in #4709, not here.
    public static bool IsReportEnabled { get; set; }
        = TestRunnerInfo.IsBuildAgent()
            || !(Environment.GetEnvironmentVariable("ActualChat_TestWaitReport") ?? "").IsNullOrEmpty();

    public static Task When(
        Func<CancellationToken, Task> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var budget = Budget(timeout, isExactTimeout);
        return Measured(() => ComputedTest.When(assertion, budget), budget, callerFilePath, callerLine);
    }

    public static Task<T> When<T>(
        Func<CancellationToken, Task<T>> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var budget = Budget(timeout, isExactTimeout);
        return Measured(() => ComputedTest.When(assertion, budget), budget, callerFilePath, callerLine);
    }

    public static Task When(
        IServiceProvider services,
        Func<CancellationToken, Task> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var budget = Budget(timeout, isExactTimeout);
        return Measured(() => ComputedTest.When(services, assertion, budget), budget, callerFilePath, callerLine);
    }

    public static Task WhenPolled(
        Action assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
        => WhenPolled(assertion, null!, timeout, isExactTimeout, callerFilePath, callerLine);

    public static Task WhenPolled(
        Action assertion,
        IEnumerable<TimeSpan> checkIntervals,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var budget = Budget(timeout, isExactTimeout);
        return Measured(() => TestExt.When(assertion, checkIntervals, budget), budget, callerFilePath, callerLine);
    }

    public static Task WhenPolled(
        Func<Task> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
        => WhenPolled(assertion, null!, timeout, isExactTimeout, callerFilePath, callerLine);

    public static Task WhenPolled(
        Func<Task> assertion,
        IEnumerable<TimeSpan> checkIntervals,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var budget = Budget(timeout, isExactTimeout);
        return Measured(() => TestExt.When(assertion, checkIntervals, budget), budget, callerFilePath, callerLine);
    }

    // The type argument has to be explicit at the call site: without it the non-generic overload
    // above wins the tie-break and the lambda's value has nowhere to go.
    public static Task<T> WhenPolled<T>(
        Func<Task<T>> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
        => WhenPolled(assertion, null!, timeout, isExactTimeout, callerFilePath, callerLine);

    public static Task<T> WhenPolled<T>(
        Func<Task<T>> assertion,
        IEnumerable<TimeSpan> checkIntervals,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var budget = Budget(timeout, isExactTimeout);
        return Measured(Poll, budget, callerFilePath, callerLine);

        async Task<T> Poll() {
            var result = default(T)!;
            await TestExt.When(async () => {
                    result = await assertion.Invoke();
                },
                checkIntervals,
                budget);
            return result;
        }
    }

    // bUnit's WaitForAssertion, scaled like every other budget here. Its own default is 1s everywhere
    public static Task WhenRendered<TComponent>(
        IRenderedComponent<TComponent> component,
        Action assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
        where TComponent : IComponent
    {
        var budget = Budget(timeout, isExactTimeout);
        return Measured(Wait, budget, callerFilePath, callerLine);

        async Task Wait() {
            try {
                await component.WaitForAssertionAsync(assertion, budget).ConfigureAwait(false);
            }
            catch (WaitForFailedException e) {
                var probeResult = await ProbeDispatcher(component).ConfigureAwait(false);
                throw new WaitForFailedException($"{e.Message}{Environment.NewLine}{probeResult}", e);
            }
        }
    }

    // Private methods

    private static TimeSpan Budget(TimeSpan? timeout, bool isExactTimeout)
        => timeout is not { } value ? DefaultTimeout.CiScaled()
            : isExactTimeout ? value
            : value.CiScaled();

    private static async Task Measured(Func<Task> wait, TimeSpan budget, string callerFilePath, int callerLine)
    {
        if (!IsReportEnabled) {
            await wait.Invoke().ConfigureAwait(false);
            return;
        }

        var startedAt = CpuTimestamp.Now;
        var isCompleted = false;
        try {
            await wait.Invoke().ConfigureAwait(false);
            isCompleted = true;
        }
        finally {
            Report(startedAt.Elapsed, budget, isCompleted, callerFilePath, callerLine);
        }
    }

    private static async Task<T> Measured<T>(Func<Task<T>> wait, TimeSpan budget, string callerFilePath, int callerLine)
    {
        if (!IsReportEnabled)
            return await wait.Invoke().ConfigureAwait(false);

        var startedAt = CpuTimestamp.Now;
        var isCompleted = false;
        try {
            var result = await wait.Invoke().ConfigureAwait(false);
            isCompleted = true;
            return result;
        }
        finally {
            Report(startedAt.Elapsed, budget, isCompleted, callerFilePath, callerLine);
        }
    }

    // A file rather than the console: xUnit captures a test's output and prints it only when the
    // test fails, so a console line would report every wait except the ones that went fine - which
    // are the ones worth measuring. One file per process, next to the test binaries.
    private static void Report(
        TimeSpan elapsed,
        TimeSpan budget,
        bool isCompleted,
        string callerFilePath,
        int callerLine)
    {
        var line = $"{elapsed.TotalMilliseconds:F0}/{budget.TotalMilliseconds:F0}ms "
            + $"{(isCompleted ? "ok" : "fail")} "
            + $"{((FilePath)callerFilePath).FileName}:{callerLine}";
        lock (ReportLock) {
            _reportWriter ??= NewReportWriter();
            _reportWriter.WriteLine(line);
        }
    }

    // The start time is in the name because a process id is reused: two runs on one machine would
    // otherwise append to the same file, and the older numbers would pass for this run's.
    private static StreamWriter NewReportWriter()
    {
        FilePath path = AppContext.BaseDirectory;
        path &= $"{ReportFileNamePrefix}{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log";
        return new StreamWriter(path, append: true) { AutoFlush = true };
    }

    // bUnit queues its checks on the renderer's dispatcher, so "Check count: 0" means the dispatcher
    // never got to one. Whether it is free right after tells a short budget from a stuck dispatcher.
    private static async Task<string> ProbeDispatcher<TComponent>(IRenderedComponent<TComponent> component)
        where TComponent : IComponent
    {
        var startedAt = CpuTimestamp.Now;
        var probeTask = component.InvokeAsync(static () => { });
        var completedTask = await Task.WhenAny(probeTask, Task.Delay(DispatcherProbeTimeout)).ConfigureAwait(false);
        return completedTask == probeTask
            ? $"Renderer dispatcher: free, a probe ran {startedAt.Elapsed.ToShortString()} after the wait failed."
            : $"Renderer dispatcher: busy, a probe didn't run in {DispatcherProbeTimeout.ToShortString()}.";
    }
}
