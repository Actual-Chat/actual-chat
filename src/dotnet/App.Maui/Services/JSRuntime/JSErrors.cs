using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public static class JSRuntimeErrors
{
    public static Exception Disconnected()
        => new JSDisconnectedException(
            "JavaScript interop calls cannot be issued at this time. " +
            "Most likely the PageContext is disconnected / being disposed.");

    public static Exception Disconnected(Exception innerException)
        => new JSException(
            "JavaScript interop call failed. " +
            "Most likely the PageContext is disconnected / being disposed.", innerException);
}
