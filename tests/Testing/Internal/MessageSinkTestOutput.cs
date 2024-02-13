using Xunit.Sdk;

namespace ActualChat.Testing.Internal;

public class MessageSinkTestOutput(IMessageSink messageSink) : ITestOutputHelper
{
    private IMessageSink MessageSink { get; } = messageSink;

    public void WriteLine(string message)
    {
        try {
            MessageSink.OnMessage(new DiagnosticMessage(message));
        }
        catch {
            // Intended
        }
    }

    public void WriteLine(string format, params object[] args)
    {
        try {
            MessageSink.OnMessage(new DiagnosticMessage(format, args));
        }
        catch {
            // Intended
        }
    }
}
