using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public static class JSRuntimeErrors
{
    private const string DisconnectedMessage =
        "JavaScript interop calls cannot be issued at this time. " +
        "Most likely the PageContext is disconnected / being disposed.";

    private const string FailedMessage =
        "JavaScript interop call failed. " +
        "Most likely the PageContext is disconnected / being disposed.";

    public static Exception Disconnected()
        => new JSDisconnectedException(DisconnectedMessage);

    public static Exception Disconnected(Exception innerException)
        => new JSException(FailedMessage, innerException);

    public static bool IsDisconnectedException(this Exception e)
        => e is JSDisconnectedException or JSException { Message: FailedMessage };
}
