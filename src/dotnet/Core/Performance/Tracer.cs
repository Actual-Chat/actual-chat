using Cysharp.Text;

namespace ActualChat.Performance;

public sealed class Tracer
{
    private readonly CpuTimestamp _startedAt;

#if DEBUG // || true
    public const bool IsDefaultTracerEnabled = true;
#else
    public const bool IsDefaultTracerEnabled = false;
#endif

    public static readonly Tracer None = new("None", null);
    public static Tracer Default { get; set; } =
        IsDefaultTracerEnabled ? new("Default", static x => Console.WriteLine("@ " + x.Format())) : None;

    public readonly string Name;
    public TimeSpan Elapsed => _startedAt.Elapsed;
    private readonly Func<bool> _isEnabled;
    public readonly Action<TracePoint>? Writer;
    public bool IsEnabled {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isEnabled();
    }

    public Tracer this[string name] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsEnabled
            ? new (ZString.Concat(Name, '.', name), _isEnabled, Writer, _startedAt)
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
    public Tracer(string name, Func<bool> isEnabled, Action<TracePoint> writer)
        : this(name, isEnabled, writer, CpuTimestamp.Now)
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Tracer(string name, Action<TracePoint>? writer, CpuTimestamp startedAt)
        : this(name, () => writer is not null, writer, startedAt)
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Tracer(string name, Func<bool> isEnabled, Action<TracePoint>? writer, CpuTimestamp startedAt)
    {
        if (name.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(name));

        Name = name;
        Writer = writer;
        _isEnabled = isEnabled;
        _startedAt = startedAt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Point(TracePoint point)
    {
        if (IsEnabled)
            Writer?.Invoke(point);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Point([CallerMemberName] string label = "")
    {
        if (IsEnabled)
            Writer?.Invoke(new TracePoint(this, label, Elapsed));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Point(string label, TimeSpan elapsed)
    {
        if (IsEnabled)
            Writer?.Invoke(new TracePoint(this, label, elapsed));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceRegion Region([CallerMemberName] string label = "", bool logEnter = true)
        => new(this, label, logEnter);
}
