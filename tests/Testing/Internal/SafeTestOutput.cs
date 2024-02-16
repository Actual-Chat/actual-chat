namespace ActualChat.Testing.Internal;

public class SafeTestOutput(ITestOutputHelper wrapped) : ITestOutputWrapper
{
    public ITestOutputHelper Wrapped { get; } = wrapped;

    public void WriteLine(string message)
    {
        try {
            Wrapped.WriteLine(message);
        }
        catch {
            // Intended
        }
    }

    public void WriteLine(string format, params object[] args)
    {
        try {
            Wrapped.WriteLine(format, args);
        }
        catch {
            // Intended
        }
    }
}
