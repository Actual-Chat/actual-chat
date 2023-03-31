using Cysharp.Text;

namespace ActualChat.Performance;

public sealed class Tracer
{
    public static Tracer None { get; } = new("None", null, /* None Tracer timer should not count */ new Stopwatch());
    public static Tracer Default { get; set; } =
#if DEBUG || DEBUG_MAUI
        new("Default", static x => Console.WriteLine("@ " + x.Format()));
#else
        None;
#endif

    private readonly Stopwatch _timer;

    public string Name { get; }
    public TimeSpan Elapsed => _timer.Elapsed;
    public Action<TracePoint>? Writer { get; }
    public bool IsEnabled => Writer != null;

    public Tracer this[string name]
        => IsEnabled
            ? new (ZString.Concat(Name, '.', name), Writer, _timer)
            : None;

    public Tracer this[Type type]
        => this[type.GetName()];

    public Tracer(string name, Action<TracePoint>? writer, Stopwatch? timer = null)
    {
        if (name.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(name));

        Name = name;
        Writer = writer;
        _timer = timer ?? Stopwatch.StartNew();
    }

    public void Point(TracePoint point)
        => Writer?.Invoke(point);

    public void Point(string label)
        => Writer?.Invoke(new TracePoint(this, label, Elapsed));

    public void Point(string label, TimeSpan elapsed)
        => Writer?.Invoke(new TracePoint(this, label, elapsed));

    public TraceRegion Region(string label, bool logEnter = true)
        => new(this, label, logEnter);
}
