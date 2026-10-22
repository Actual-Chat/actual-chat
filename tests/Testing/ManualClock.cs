namespace ActualChat.Testing;

/// <summary>
/// A <see cref="MomentClock"/> whose time moves only when a test sets <see cref="Time"/>:
/// a <see cref="Delay"/> ends once the time reaches its due moment, however long it waits in real time.
/// </summary>
public sealed class ManualClock : MomentClock
{
    private readonly Lock _lock = new();
    private readonly List<(Moment DueAt, TaskCompletionSource WhenDue)> _delays = [];
    // Starts well past the epoch: code often reads default(Moment) as "never happened"
    private Moment _time = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public override Moment Now => Time;

    public Moment Time {
        get {
            lock (_lock)
                return _time;
        }
        set {
            List<TaskCompletionSource> dueDelays;
            lock (_lock) {
                _time = value;
                dueDelays = _delays.Where(x => x.DueAt <= value).Select(x => x.WhenDue).ToList();
                _delays.RemoveAll(x => x.DueAt <= value);
            }
            foreach (var whenDue in dueDelays)
                whenDue.TrySetResult();
        }
    }

    public override Task Delay(TimeSpan dueIn, CancellationToken cancellationToken = default)
    {
        if (dueIn == System.Threading.Timeout.InfiniteTimeSpan)
            return Task.Delay(dueIn, cancellationToken);
        if (dueIn <= TimeSpan.Zero)
            return Task.CompletedTask;

        var whenDue = TaskCompletionSourceExt.New();
        lock (_lock)
            _delays.Add((_time + dueIn, whenDue));
        return whenDue.Task.WaitAsync(cancellationToken);
    }
}
