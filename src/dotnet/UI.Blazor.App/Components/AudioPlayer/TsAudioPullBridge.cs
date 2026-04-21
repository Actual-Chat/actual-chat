using System.Globalization;
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// .NET wrapper around <c>window.blazorApp.LiveAudioPullBridge</c> — the
/// TS-side entry point for the pull-based audio path (which subscribes to
/// <c>ILiveAudioStreams</c> directly from TypeScript and renders via
/// <c>PullAudioRenderer</c>, skipping the Blazor frame-push interop).
///
/// Each Start* call returns a numeric token owned by JS; pass it back to
/// <see cref="Stop"/> to cancel. Cancellation safety: the .NET
/// <see cref="CancellationToken"/> passed into Start* is monitored and calls
/// <see cref="Stop"/> automatically when cancelled, so callers can scope
/// lifetime with a linked CTS.
/// </summary>
public sealed class TsAudioPullBridge(IJSRuntime js, ILogger log)
{
    private static readonly string Prefix = $"{BlazorUIAppModule.ImportName}.LiveAudioPullBridge";

    /// <summary>
    /// Starts a live listen session. Returns a disposable handle — dispose it
    /// (or cancel <paramref name="cancellationToken"/>) to stop.
    /// </summary>
    public async Task<TsAudioPullHandle> StartListen(
        Session session,
        ChatId chatId,
        AuthorId? ownAuthorId,
        CancellationToken cancellationToken)
    {
        var token = await js.InvokeAsync<long>(
                $"{Prefix}.startListen",
                cancellationToken,
                session.Id, chatId.Value, ownAuthorId?.Value)
            .ConfigureAwait(false);
        log.LogDebug("StartListen chatId={ChatId} ownAuthor={OwnAuthor} -> token={Token}",
            chatId, ownAuthorId, token);
        return new TsAudioPullHandle(this, token, cancellationToken);
    }

    /// <summary>
    /// Starts a replay session. <paramref name="startAt"/> is wall-clock origin;
    /// <paramref name="rewindOffset"/> trims from that moment; <paramref name="speed"/>
    /// is the playback rate (1.0 = normal). Returns a disposable handle.
    /// </summary>
    public async Task<TsAudioPullHandle> StartReplay(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed,
        AuthorId? ownAuthorId,
        CancellationToken cancellationToken)
    {
        // Moment ticks exceed Number.MAX_SAFE_INTEGER (2^53) for any moment
        // from ~year 2000 onward; JSON interop's number serialization would
        // lose precision. Send as decimal string; JS side converts to BigInt
        // before msgpack encoding.
        var startAtTicks = startAt.EpochOffsetTicks.ToString(CultureInfo.InvariantCulture);
        var rewindTicks = rewindOffset.Ticks.ToString(CultureInfo.InvariantCulture);
        var token = await js.InvokeAsync<long>(
                $"{Prefix}.startReplay",
                cancellationToken,
                session.Id,
                chatId.Value,
                startAtTicks,
                rewindTicks,
                speed,
                ownAuthorId?.Value)
            .ConfigureAwait(false);
        log.LogDebug(
            "StartReplay chatId={ChatId} startAt={StartAt} rewindOffset={RewindOffset} speed={Speed} ownAuthor={OwnAuthor} -> token={Token}",
            chatId, startAt, rewindOffset, speed, ownAuthorId, token);
        return new TsAudioPullHandle(this, token, cancellationToken);
    }

    internal async Task Stop(long token)
    {
        try {
            await js.InvokeVoidAsync($"{Prefix}.stop", CancellationToken.None, token)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            log.LogWarning(e, "Stop(token={Token}) failed", token);
        }
    }
}

/// <summary>
/// Handle to a running TS-pull session. Disposes automatically when the
/// supplied <see cref="CancellationToken"/> is cancelled; otherwise call
/// <see cref="DisposeAsync"/> to stop.
/// </summary>
public sealed class TsAudioPullHandle : IAsyncDisposable
{
    private readonly TsAudioPullBridge _bridge;
    private readonly long _token;
    private readonly CancellationTokenRegistration _registration;
    private int _disposed;

    public long Token => _token;

    internal TsAudioPullHandle(TsAudioPullBridge bridge, long token, CancellationToken cancellationToken)
    {
        _bridge = bridge;
        _token = token;
        // Fire-and-forget on cancellation; ignore errors.
        _registration = cancellationToken.Register(() => {
            _ = DisposeAsync();
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _registration.Dispose();
        await _bridge.Stop(_token).ConfigureAwait(false);
    }
}
