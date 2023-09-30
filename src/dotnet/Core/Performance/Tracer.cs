using Cysharp.Text;

namespace ActualChat.Performance;

public sealed class Tracer
{
    private readonly CpuTimestamp _startedAt;

    public static readonly Tracer None = new("None", null);
    public static Tracer Default { get; set; } =
#if DEBUG
        new("Default", static x => Console.WriteLine("@ " + x.Format()));
#else
        None;
#endif

    public readonly string Name;
    public TimeSpan Elapsed => _startedAt.Elapsed;
    public readonly Action<TracePoint>? Writer;
    public bool IsEnabled {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Writer != null;
    }

    public Tracer this[string name] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsEnabled
            ? new (ZString.Concat(Name, '.', name), Writer, _startedAt)
            : None;
    }

    public Tracer this[Type type] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this[type.GetName()];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Tracer(string name, Action<TracePoint>? writer)
        : this(name, writer, CpuTimestamp.Now)
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Tracer(string name, Action<TracePoint>? writer, CpuTimestamp startedAt)
    {
        if (name.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(name));

        Name = name;
        Writer = writer;
        _startedAt = startedAt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Point(TracePoint point)
        => Writer?.Invoke(point);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Point(string label)
        => Writer?.Invoke(new TracePoint(this, label, Elapsed));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Point(string label, TimeSpan elapsed)
        => Writer?.Invoke(new TracePoint(this, label, elapsed));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceRegion Region([CallerMemberName] string label = "", bool logEnter = true)
        => new(this, label, logEnter);
}
