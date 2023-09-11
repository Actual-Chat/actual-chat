using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public static class JSRuntimeErrors
{
    public static Exception Disconnected()
        => new JSDisconnectedException(
            "JavaScript interop calls cannot be issued at this time. " +
            "Most likely the PageContext has disconnected and is being disposed.");

    public static Exception Disconnected(Exception innerException)
        => new JSException(
            "JavaScript interop call failed. " +
            "Most likely the PageContext has disconnected and is being disposed.", innerException);
}
