using Cysharp.Text;

namespace ActualChat.Performance;

public readonly struct TraceRegion : IDisposable
{
    public readonly Tracer Tracer;
    public readonly string Label;
    public readonly TimeSpan StartedAt;
    public readonly bool LogEnter;

    public TraceRegion(Tracer tracer, string label, bool logEnter = false)
    {
        Tracer = tracer;
        Label = label;
        LogEnter = logEnter;
        StartedAt = tracer.Elapsed;
        if (logEnter && Tracer.IsEnabled)
            Tracer.Point(ZString.Concat("-> ", label), StartedAt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IDisposable.Dispose()
        => Close();

    public void Close()
    {
        if (!Tracer.IsEnabled)
            return;

        var elapsed = Tracer.Elapsed;
        var duration = elapsed - StartedAt;

        string endLabel;
        var sb = ZString.CreateStringBuilder(true);
        try {
            if (LogEnter)
                sb.Append("<- ");
            sb.Append(Label);
            sb.Append(" - took ");
            TracePoint.FormatDuration(duration, ref sb);
            endLabel = sb.ToString();
        }
        finally {
            sb.Dispose();
        }
        Tracer.Point(endLabel);
    }
}
