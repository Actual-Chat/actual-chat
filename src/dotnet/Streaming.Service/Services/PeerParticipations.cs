using ActualChat.Live;
using ActualChat.Streaming.Module;
using ActualLab.Locking;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Releases the participations a client claimed over RPC once its peer stays disconnected
/// past <see cref="StreamingSettings.ParticipationDisconnectGrace"/>: a killed app sends no
/// leave, and its 90s staleness window would keep suppressing PTT wakes for that author.
/// </summary>
public sealed class PeerParticipations(IServiceProvider services)
{
    private static readonly RetryDelaySeq ReleaseRetryDelays = RetryDelaySeq.Exp(0.5, 4);
    private const int MaxReleaseAttemptCount = 3;

    private sealed class Entry
    {
        public readonly Dictionary<ChatId, (AuthorId AuthorId, ParticipationKind Kind)> Items = new();
        public bool IsWatched;
        public bool IsReleased;
    }

    private readonly ConcurrentDictionary<RpcPeer, Entry> _entries = new();
    // Serializes a peer's claim + write against the release of the same author's
    // participation, so a release can never overwrite a claim that raced past it.
    private readonly AsyncLockSet<(ChatId ChatId, AuthorId AuthorId)> _keyLocks = new(LockReentryMode.CheckedFail);

    private IServiceProvider Services { get; } = services;
    private StreamingSettings Settings => field ??= Services.GetRequiredService<StreamingSettings>();
    private ILiveSessionsBackend Backend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task SetParticipation(
        RpcPeer? peer,
        ChatId chatId,
        AuthorId authorId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken)
    {
        if (peer is null) {
            await Backend.SetParticipation(chatId, authorId, kind, isActive, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var _ = await _keyLocks.Lock((chatId, authorId), cancellationToken).ConfigureAwait(false);
        Claim(peer, chatId, authorId, kind, isActive);
        await Backend.SetParticipation(chatId, authorId, kind, isActive, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private void Claim(RpcPeer peer, ChatId chatId, AuthorId authorId, ParticipationKind kind, bool isActive)
    {
        while (true) {
            var entry = _entries.GetOrAdd(peer, static _ => new Entry());
            var mustWatch = false;
            lock (entry) {
                if (entry.IsReleased)
                    continue; // Released between GetOrAdd and the lock; the next GetOrAdd starts a fresh entry

                if (!entry.IsWatched)
                    entry.IsWatched = mustWatch = true;
                if (isActive)
                    entry.Items[chatId] = (authorId, kind);
                else
                    entry.Items.Remove(chatId);
            }
            if (mustWatch)
                _ = Watch(peer, entry);
            return;
        }
    }

    private bool IsClaimed(ChatId chatId, AuthorId authorId)
    {
        foreach (var entry in _entries.Values)
            lock (entry) {
                if (!entry.IsReleased && entry.Items.TryGetValue(chatId, out var item) && item.AuthorId == authorId)
                    return true;
            }
        return false;
    }

    private async Task Watch(RpcPeer peer, Entry entry)
    {
        try {
            while (true) {
                await peer.ConnectionState.When(x => !x.IsConnected()).ConfigureAwait(false);
                await Clocks.CpuClock.Delay(Settings.ParticipationDisconnectGrace).ConfigureAwait(false);
                if (!peer.ConnectionState.Value.IsConnected())
                    break;
            }
        }
        catch (Exception e) {
            Log.LogWarning(e, "Peer {Peer}: connection watch failed, releasing its participations", peer.Ref);
        }
        await Release(peer, entry).ConfigureAwait(false);
    }

    private async Task Release(RpcPeer peer, Entry entry)
    {
        KeyValuePair<ChatId, (AuthorId AuthorId, ParticipationKind Kind)>[] items;
        lock (entry) {
            entry.IsReleased = true;
            items = entry.Items.ToArray();
            entry.Items.Clear();
        }
        _entries.TryRemove(new KeyValuePair<RpcPeer, Entry>(peer, entry));
        if (items.Length == 0)
            return;

        Log.LogInformation("Peer {Peer} is gone, releasing {Count} participation(s)", peer.Ref, items.Length);
        foreach (var (chatId, (authorId, kind)) in items) {
            using var _ = await _keyLocks.Lock((chatId, authorId), CancellationToken.None).ConfigureAwait(false);
            if (IsClaimed(chatId, authorId))
                continue; // Another device of the same account (or this peer, reconnected) still holds it

            await ReleaseOne(peer, chatId, authorId, kind).ConfigureAwait(false);
        }
    }

    private async Task ReleaseOne(RpcPeer peer, ChatId chatId, AuthorId authorId, ParticipationKind kind)
    {
        for (var attempt = 1;; attempt++)
            try {
                await Backend.SetParticipation(chatId, authorId, kind, false, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (Exception e) {
                if (attempt >= MaxReleaseAttemptCount) {
                    Log.LogWarning(e, "Peer {Peer}: failed to release {Kind} participation of {AuthorId} in {ChatId}",
                        peer.Ref, kind, authorId, chatId);
                    return;
                }
                await Clocks.CpuClock.Delay(ReleaseRetryDelays[attempt], CancellationToken.None).ConfigureAwait(false);
            }
    }
}
