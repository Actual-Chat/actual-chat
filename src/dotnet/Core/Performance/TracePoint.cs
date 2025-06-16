using System.Text;

namespace ActualChat.Performance;

public readonly record struct TracePoint(Tracer Tracer, string Label, TimeSpan Elapsed)
{
    public override string ToString()
        => Format();

    public string Format()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append(Tracer.Name);
        sb.Append(": ");
        FormatDuration(Elapsed, sb);
        sb.Append(' ');
        sb.Append(Label);
        return sb.ToStringAndRelease();
    }

    public static string FormatDuration(TimeSpan duration)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        FormatDuration(duration, sb);
        return sb.ToStringAndRelease();
    }

    public static void FormatDuration(TimeSpan duration, StringBuilder sb)
        => sb.AppendFormat(CultureInfo.InvariantCulture, "{0:N3}s", duration.TotalSeconds);
}
