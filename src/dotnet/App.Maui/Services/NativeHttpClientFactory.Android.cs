using Java.Util.Concurrent;

namespace ActualChat.App.Maui.Services;

public partial class NativeHttpClientFactory
{
    private partial HttpMessageHandler? CreatePlatformMessageHandler()
        => new CronetMessageHandler(Services.GetRequiredService<IExecutorService>());
}
