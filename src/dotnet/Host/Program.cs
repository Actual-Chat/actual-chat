using System.Text;
using ActualChat.Hosting;

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
        Constants.HostInfo = appHost.Services.GetRequiredService<HostInfo>();
        await appHost.Initialize().ConfigureAwait(false);
        await appHost.Run().ConfigureAwait(false);
    }
}
