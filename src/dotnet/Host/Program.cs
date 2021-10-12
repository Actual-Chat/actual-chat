using ActualChat.Audio;
using ActualChat.Host;

namespace ActualChat.Host;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        SourceAudioProcessor.SkipAutoStart = false;
        using var appHost = new AppHost();
        await appHost.Build();
        await appHost.Initialize(true);
        await appHost.Run();
    }
}


