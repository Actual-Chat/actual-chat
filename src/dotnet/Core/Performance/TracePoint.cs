using Cysharp.Text;

namespace ActualChat.Performance;

public readonly record struct TracePoint(Tracer Tracer, string Label, TimeSpan Elapsed)
{
    public override string ToString()
        => Format();

    public string Format()
    {
        var sb = ZString.CreateStringBuilder(true);
        try {
            sb.Append(Tracer.Name);
            sb.Append(": ");
            FormatDuration(Elapsed, ref sb);
            sb.Append(' ');
            sb.Append(Label);
            return sb.ToString();
        }
        finally {
            sb.Dispose();
        }
    }

    public static string FormatDuration(TimeSpan duration)
    {
        var sb = ZString.CreateStringBuilder(true);
        try {
            FormatDuration(duration, ref sb);
            return sb.ToString();
        }
        finally {
            sb.Dispose();
        }
    }

    public static void FormatDuration(TimeSpan duration, ref Utf16ValueStringBuilder sb)
        => sb.AppendFormat("{0:N3}s", duration.TotalSeconds);
}
