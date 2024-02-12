namespace ActualChat.Testing;

public sealed class NullTestOutput : ITestOutputHelper
{
    public static readonly NullTestOutput Instance = new();

    public void WriteLine(string message) { }
    public void WriteLine(string format, params object[] args) { }
}
