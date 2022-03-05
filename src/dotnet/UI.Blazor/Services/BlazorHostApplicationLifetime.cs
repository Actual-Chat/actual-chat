using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.Services;

public class BlazorHostApplicationLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted { get; }
    public CancellationToken ApplicationStopping { get; }
    public CancellationToken ApplicationStopped { get; }

    public BlazorHostApplicationLifetime()
    {
        ApplicationStarted = new CancellationToken(true);
        ApplicationStopping = default;
        ApplicationStopped = default;
    }

    public void StopApplication()
    { }
}
