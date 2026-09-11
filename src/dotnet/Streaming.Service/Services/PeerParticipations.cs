using ActualChat.Live;
using ActualChat.Streaming.Module;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Releases the participations a client claimed over RPC once its peer stays disconnected
/// past <see cref="StreamingSettings.ParticipationDisconnectGrace"/>: a killed app sends no
/// leave, and its 90s staleness window would keep suppressing PTT wakes for that author.
/// </summary>
public sealed class PeerParticipations(IServiceProvider services)
{
    private sealed class Entry
    {
        public readonly Dictionary<ChatId, (AuthorId AuthorId, ParticipationKind Kind)> Items = new();
        public bool IsReleased;
    }

    private readonly ConcurrentDictionary<RpcPeer, Entry> _entries = new();

    private IServiceProvider Services { get; } = services;
    private StreamingSettings Settings => field ??= Services.GetRequiredService<StreamingSettings>();
    private ILiveSessionsBackend Backend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public void Set(RpcPeer peer, ChatId chatId, AuthorId authorId, ParticipationKind kind, bool isActive)
    {
        while (true) {
            var entry = _entries.GetOrAdd(peer, static _ => new Entry());
            var isNew = false;
            lock (entry) {
                if (entry.IsReleased)
                    continue; // Released between GetOrAdd and the lock; the next GetOrAdd starts a fresh entry

                isNew = entry.Items.Count == 0;
                if (isActive)
                    entry.Items[chatId] = (authorId, kind);
                else
                    entry.Items.Remove(chatId);
            }
            if (isNew && isActive)
                _ = Watch(peer, entry);
            return;
        }
    }

    // Private methods

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
        foreach (var (chatId, (authorId, kind)) in items)
            try {
                await Backend.SetParticipation(chatId, authorId, kind, false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Peer {Peer}: failed to release {Kind} participation of {AuthorId} in {ChatId}",
                    peer.Ref, kind, authorId, chatId);
            }
    }
}
