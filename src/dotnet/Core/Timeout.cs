namespace ActualChat;

public readonly record struct Timeout(
    IMomentClock Clock,
    TimeSpan Duration)
{
    public override string ToString()
        => $"{GetType().Name}({Duration.ToShortString()})";

    public Task Wait(CancellationToken cancellationToken = default)
        => Clock.Delay(Duration, cancellationToken);

    public async Task WaitAndThrow(CancellationToken cancellationToken = default)
    {
        await Wait(cancellationToken).ConfigureAwait(false);
        throw new TimeoutException();
    }
}
