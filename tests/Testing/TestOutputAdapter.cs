using Xunit.Sdk;

namespace ActualChat.Testing;

public class TestOutputAdapter(IMessageSink messageSink) : ITestOutputHelper
{
    private IMessageSink MessageSink { get; } = messageSink;

    public void WriteLine(string message)
        => MessageSink.OnMessage(new DiagnosticMessage(message));

    public void WriteLine(string format, params object[] args)
        => MessageSink.OnMessage(new DiagnosticMessage(format, args));
}
