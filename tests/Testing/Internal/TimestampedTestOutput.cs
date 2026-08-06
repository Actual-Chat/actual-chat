namespace ActualChat.Testing.Internal;

public class TimestampedTestOutput(ITestOutputHelper wrapped) : ITestOutputWrapper
{
    private static readonly string[] Indents = Enumerable.Range(0, 32)
        .Select(x => Environment.NewLine + new string(' ', x))
        .ToArray();
    private readonly CpuTimestamp _startedAt = CpuTimestamp.Now;

    public ITestOutputHelper Wrapped { get; } = wrapped;

    public void WriteLine(string message)
    {
        FormattableString prefixFormat = $"{_startedAt.Elapsed.TotalSeconds:F3} ";
        var prefix = prefixFormat.ToString();
        message = message.Replace(Environment.NewLine, Indents[prefix.Length]);
        Wrapped.WriteLine(prefix + message);
    }

    public void WriteLine(string format, params object[] args)
    {
        FormattableString prefixFormat = $"{_startedAt.Elapsed.TotalSeconds:F3} ";
        var prefix = prefixFormat.ToString();
        var message = string.Format(format, args)
            .Replace(Environment.NewLine, Indents[prefix.Length]);
        Wrapped.WriteLine(prefix + message);
    }
}
