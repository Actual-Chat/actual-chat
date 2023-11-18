namespace ActualChat.App.Maui.Services;

public partial class NativeHttpClientFactory
{
    // Not used, but required to compile without errors
#pragma warning disable CA1822 // Can be static
    private partial HttpMessageHandler? CreatePlatformMessageHandler()
        => null;
#pragma warning restore CA1822
}
