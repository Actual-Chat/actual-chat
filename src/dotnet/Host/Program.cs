using System.Text;

namespace ActualChat.Host;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        Console.OutputEncoding = Encoding.UTF8;
        using var appHost = new AppHost();
        await appHost.Build().ConfigureAwait(false);
        await appHost.Initialize().ConfigureAwait(false);
        await appHost.Run().ConfigureAwait(false);
    }
}
