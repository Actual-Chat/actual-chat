namespace ActualChat.Performance;

public readonly struct TraceRegion : IDisposable
{
    public readonly Tracer Tracer;
    public readonly string Label;
    public readonly TimeSpan StartedAt;
    public readonly bool LogEnter;
    public readonly bool IsEnabled;

    public TraceRegion(Tracer tracer, string label, bool logEnter = false)
    {
        Tracer = tracer;
        Label = label;
        LogEnter = logEnter;
        StartedAt = tracer.Elapsed;
        IsEnabled = Tracer.IsEnabled;
        if (logEnter && IsEnabled)
            Tracer.Point(string.Concat("-> ", label), StartedAt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IDisposable.Dispose()
        => Close();

    public void Close()
    {
        if (!IsEnabled)
            return;

        var elapsed = Tracer.Elapsed;
        var duration = elapsed - StartedAt;

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        if (LogEnter)
            sb.Append("<- ");
        sb.Append(Label);
        sb.Append(" - took ");
        TracePoint.FormatDuration(duration, sb);
        Tracer.Point(sb.ToStringAndRelease());
    }
}
