using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

/// <summary>
/// Holds Run() for a target until the test opens its gate, so the test can look at
/// what the store holds while the flow's first resume is still in progress.
/// </summary>
[Flow(DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class GatedThrottledUpdateFlow : ThrottledUpdateFlow
{
    private static readonly ConcurrentDictionary<string, Gate> Gates = new();
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<Moment>> Resumes = new();

    protected override TimeSpan ThrottlePeriod => TimeSpan.FromSeconds(2);

    public static Gate Close(string target)
        => Gates.GetOrAdd(target, static t => new Gate(t));

    public static Moment[] GetResumeTimes(string target)
        => Resumes.TryGetValue(target, out var resumes) ? resumes.ToArray() : [];

    protected override ValueTask Resume(CancellationToken cancellationToken)
    {
        // Counted outside the flow's state: with two chains, its stored console lost a resume's line
        Resumes.GetOrAdd(Target, static _ => new()).Enqueue(ResumedAt);
        return base.Resume(cancellationToken);
    }

    protected override async ValueTask Run(CancellationToken cancellationToken)
    {
        if (Gates.TryGetValue(Target, out var gate)) {
            gate.Entered.TrySetResult();
            await gate.Opened.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        Console.Log($"Run: Target={Target}");
    }

    // Nested types

    public sealed class Gate(string target) : IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open()
            => Opened.TrySetResult();

        public void Dispose()
        {
            // A test failing before Open() would leave Run() holding the flow's resume lock
            Open();
            Gates.TryRemove(target, out _);
        }
    }
}
