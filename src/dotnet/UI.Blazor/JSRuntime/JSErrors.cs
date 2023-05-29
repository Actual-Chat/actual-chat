namespace ActualChat.UI.Blazor;

public static class JSRuntimeErrors
{
    public static Exception Disconnected()
        => new JSDisconnectedException(
            "JavaScript interop calls cannot be issued at this time. " +
            "Most likely the PageContext has disconnected and is being disposed.");

}
