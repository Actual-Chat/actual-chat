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
        => CancellationTokenSource.CreateLinkedTokenSource(
            hostLifetime?.ApplicationStopping ?? default,
            cancellationToken);

    public static async Task WhenStarted(
        this IHostApplicationLifetime? hostLifetime,
        CancellationToken cancellationToken = default)
    {
        if (hostLifetime is null || hostLifetime.ApplicationStarted.IsCancellationRequested)
            return;

        using var startedOrCancelledCts = cancellationToken.LinkWith(hostLifetime.ApplicationStarted);
        await TaskExt.NeverEnding(startedOrCancelledCts.Token).SilentAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
