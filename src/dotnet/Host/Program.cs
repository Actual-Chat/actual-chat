using System.Text;
using ActualChat.Audio;

namespace ActualChat.Host;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        SourceAudioProcessor.SkipAutoStart = false;
        using var appHost = new AppHost();
        await appHost.Build();
        await appHost.Initialize();
        await appHost.Run();
    }
}
