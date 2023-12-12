using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.Services.Internal;

public class FakeHostApplicationLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted { get; } = new(true);
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication() { }
}
