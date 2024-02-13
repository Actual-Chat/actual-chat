using Cysharp.Text;

namespace ActualChat.Testing.Internal;

public class TimestampedTestOutput(ITestOutputHelper wrapped) : ITestOutputWrapper
{
    private readonly CpuTimestamp _startedAt = CpuTimestamp.Now;

    public ITestOutputHelper Wrapped { get; } = wrapped;

    public void WriteLine(string message)
        => Wrapped.WriteLine(ZString.Format("{0,5} {1}", _startedAt.Elapsed, message));

    public void WriteLine(string format, params object[] args)
        => Wrapped.WriteLine(
            ZString.Concat($"{{{args.Length},5}} ", format),
            args.Concat(Enumerable.Repeat((object)_startedAt.Elapsed, 1)).ToArray());
}
