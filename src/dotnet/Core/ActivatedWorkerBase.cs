namespace ActualChat;

public abstract class ActivatedWorkerBase(IServiceProvider services) : WorkerBase
{
    private TaskCompletionSource _whenResumeSource = new();
    private ILogger? _log;

    protected RandomTimeSpan PostActivationDelay { get; init; } = TimeSpan.FromSeconds(0.1).ToRandom(0.2);
    protected RandomTimeSpan UnconditionalActivationPeriod { get; init; } = TimeSpan.FromMinutes(5).ToRandom(0.1);
    protected RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(0.1, 10);

    protected IServiceProvider Services { get; } = services;
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    public void Activate()
    {
        lock (Lock) {
            _whenResumeSource.TrySetResult();
            _whenResumeSource = new();
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(ActivateAndSleep)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelays, Log)
            .CycleForever()
            .Run(cancellationToken);

    protected abstract Task<bool> OnActivate(CancellationToken cancellationToken);

    // Private methods

    private async Task ActivateAndSleep(CancellationToken cancellationToken)
    {
        Task whenResume;
        while (true) {
            lock (Lock) {
                if (_whenResumeSource.Task.IsCompleted)
                    _whenResumeSource = new();
                whenResume = _whenResumeSource.Task;
            }
            var isCompleted = await OnActivate(cancellationToken).ConfigureAwait(false);
            await Task.Delay(PostActivationDelay.Next(), cancellationToken).ConfigureAwait(false);
            if (isCompleted)
                break;
        }
        await whenResume.WaitAsync(UnconditionalActivationPeriod.Next(), cancellationToken).SilentAwait();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
