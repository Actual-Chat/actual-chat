using Microsoft.Extensions.Hosting;

namespace ActualChat;

public static class HostApplicationLifetimeExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CancellationToken StopToken(this IHostApplicationLifetime? hostLifetime)
        => hostLifetime?.ApplicationStopping ?? default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CancellationTokenSource CreateStopTokenSource(
        this IHostApplicationLifetime? hostLifetime,
        CancellationToken cancellationToken = default)
        => CancellationTokenSource.CreateLinkedTokenSource(hostLifetime?.ApplicationStopping ?? default, cancellationToken);
}
