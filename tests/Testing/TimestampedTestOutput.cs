using System.Diagnostics;
using Cysharp.Text;

namespace ActualChat.Testing;

public class TimestampedTestOutput : ITestOutputHelper
{
    private readonly Stopwatch _stopwatch;

    public ITestOutputHelper Output { get; }

    public TimestampedTestOutput(ITestOutputHelper output)
    {
        Output = output;
        _stopwatch = new Stopwatch();
        _stopwatch.Start();
    }

    public void WriteLine(string message)
        => Output.WriteLine(ZString.Format("{0,5} {1}", _stopwatch.ElapsedMilliseconds, message));

    public void WriteLine(string format, params object[] args)
        => Output.WriteLine(ZString.Concat($"{{{args.Length},5}} ", format), args
            .Concat(Enumerable.Repeat((object)_stopwatch.ElapsedMilliseconds, 1))
            .ToArray());
}
