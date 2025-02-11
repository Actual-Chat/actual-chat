using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public static class JSRuntimeErrors
{
    private const string Message1 = "JavaScript interop calls cannot be issued at this time. " +
            "Most likely the PageContext is disconnected / being disposed.";

    private const string Message2 = "JavaScript interop call failed. " +
            "Most likely the PageContext is disconnected / being disposed.";

    public static Exception Disconnected()
        => new JSDisconnectedException(Message1);

    public static Exception Disconnected(Exception innerException)
        => new JSException(Message2, innerException);

    public static bool IsDisconnectedException(this Exception e)
        => e is JSDisconnectedException ||
        (e is JSException jSException && OrdinalEquals(jSException.Message, Message2));
}
