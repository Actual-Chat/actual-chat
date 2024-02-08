using Cysharp.Text;

namespace ActualChat.Testing;

public class TimestampedTestOutput(ITestOutputHelper output) : ITestOutputHelper
{
    private readonly CpuTimestamp _startedAt = CpuTimestamp.Now;

    public ITestOutputHelper Output { get; } = output;

    public void WriteLine(string message)
        => Output.WriteLine(ZString.Format("{0,5} {1}", _startedAt.Elapsed, message));

    public void WriteLine(string format, params object[] args)
        => Output.WriteLine(
            ZString.Concat($"{{{args.Length},5}} ", format),
            args.Concat(Enumerable.Repeat((object)_startedAt.Elapsed, 1)).ToArray());
}
