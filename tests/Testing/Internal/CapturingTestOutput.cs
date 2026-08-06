namespace ActualChat.Testing.Internal;

public class CapturingTestOutput(ITestOutputHelper wrapped) : ITestOutputWrapper
{
    private readonly ConcurrentQueue<string> _messages = new();

    public ITestOutputHelper Wrapped { get; } = wrapped;
    public IReadOnlyList<string> Messages => _messages.ToArray();

    public bool HasMessage(string substring)
        => _messages.Any(m => m.Contains(substring));

    public void WriteLine(string message)
    {
        _messages.Enqueue(message);
        Wrapped.WriteLine(message);
    }

    public void WriteLine(string format, params object[] args)
    {
        _messages.Enqueue(string.Format(format, args));
        Wrapped.WriteLine(format, args);
    }
}
