using System.Threading.Channels;
using ActualChat.Streaming;
using ActualChat.Mesh;

namespace ActualChat.Chat;

/// <summary>
/// Holds the open producer side of every call-driven entry stream this node started, keyed by a
/// handle whose <see cref="StreamId.NodeRef"/> routes later calls back here.
/// </summary>
public class ChatEntryStreams(IServiceProvider services) : IChatEntryStreamsBackend, IDisposable
{
    private readonly ConcurrentDictionary<Symbol, ExpiringEntry<Symbol, Lease>> _leases = new();

    private IServiceProvider Services { get; } = services;
    private TextEntryStreamer Streamer => field ??= Services.GetRequiredService<TextEntryStreamer>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAudioStreamingBackend StreamingBackend
        => field ??= Services.GetRequiredService<IAudioStreamingBackend>();
    private IMaintenancesBackend Maintenances => field ??= Services.GetRequiredService<IMaintenancesBackend>();
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private ILogger Log => field ??= Services.LogFor(GetType());

    // Settable so tests don't have to wait out the real timeout
    public TimeSpan IdleTimeout { get; set; } = Constants.Chat.EntryStreamIdleTimeout;

    public void Dispose()
    {
        foreach (var expiringLease in _leases.Values.ToList())
            expiringLease.Dispose();
    }

    public virtual async Task<ChatEntryStream> Start(
        ChatId chatId,
        AuthorId authorId,
        UserId userId,
        long? localId,
        bool? isViaApi,
        Language? language,
        CancellationToken cancellationToken)
    {
        var entryToUpdate = localId is { } vLocalId
            ? await ChatsBackend
                .GetEntry(ChatEntryId.New(chatId, vLocalId), cancellationToken)
                .Require(ChatEntry.MustNotBeRemoved)
                .ConfigureAwait(false)
            : null;

        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        var lease = new Lease(streamId, userId);
        // The hard cap belongs to the lease rather than to this call: the producer is gone by the
        // time Start returns, so nothing else would ever stop a stream that is never finished.
        var stopToken = lease.StopTokenSource.Token;
        var chunks = lease.Chunks.Reader
            .ReadAllAsync(stopToken)
            .RequireAvailable(Maintenances, chatId, stopToken);
        lease.StreamTask = Streamer.Stream(
            chatId, authorId, entryToUpdate, chunks, stopToken,
            isViaApi: isViaApi, entryCreatedSource: lease.EntryCreatedSource, language: language);

        try {
            var entry = await lease.EntryCreatedSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            lease.EntryId = entry.Id;
            // The id speech is served on, which the producer never sees - it is the entry's own
            // content stream, not this lease's handle.
            lease.ContentStreamId = entry.ContentStreamId;
        }
        catch {
            lease.Dispose();
            throw;
        }

        var expiringLease = ExpiringEntry
            .New(_leases, streamId.Value, lease)
            .SetDisposer(e => {
                if (!e.Value.IsCompleted) {
                    Log.LogWarning("Entry stream #{StreamId} was abandoned - finalizing {EntryId}",
                        e.Value.Id, e.Value.EntryId);
                }
                e.Value.Dispose();
            });
        if (!_leases.TryAdd(streamId.Value, expiringLease)) {
            lease.Dispose();
            throw StandardError.Internal("Duplicate entry stream id.");
        }

        expiringLease.BumpExpiresAt(IdleTimeout).BeginExpire();
        return await WithSpeechBacklog(lease, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ChatEntryStream> Append(
        StreamId streamId,
        UserId userId,
        int offset,
        string text,
        CancellationToken cancellationToken)
    {
        var expiringLease = GetOwnLease(streamId, userId);
        var lease = expiringLease.Value;
        lock (lease.Lock) {
            if (lease.IsCompleted)
                throw StandardError.Constraint("This entry stream is already finished.");

            // Same contract as Uploads_Append: a mismatched offset writes nothing and reports
            // where the server is, so a client that retried or lost a response can resume.
            if (offset == lease.Offset) {
                if (lease.Offset + text.Length > Constants.Chat.MaxEntryTextLength)
                    throw StandardError.Constraint(
                        $"A message can hold up to {Constants.Chat.MaxEntryTextLength} characters.");

                if (!text.IsNullOrEmpty()) {
                    if (!lease.Chunks.Writer.TryWrite(text))
                        throw StandardError.Constraint("This entry stream is already finished.");

                    lease.Offset += text.Length;
                }
            }
        }

        expiringLease.BumpExpiresAt(IdleTimeout);
        return await WithSpeechBacklog(lease, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ChatEntryStream> Finish(
        StreamId streamId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var expiringLease = GetOwnLease(streamId, userId);
        var lease = expiringLease.Value;
        lease.Chunks.Writer.TryComplete();
        var entry = await lease.StreamTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (lease.Lock) {
            lease.EntryId = entry.Id;
            lease.IsCompleted = true;
        }
        // Kept for the idle timeout rather than dropped, so a retried finish answers with the
        // same result instead of "unknown stream".
        expiringLease.BumpExpiresAt(IdleTimeout);
        return lease.ToModel();
    }

    // Private methods

    // A producer that wants to be heard needs to know whether it is outrunning the voice reading
    // it, and no constant can tell it: speaking rate depends on the language, the voice and the
    // provider. Null means nothing is speaking this entry - nobody is listening.
    private async Task<ChatEntryStream> WithSpeechBacklog(Lease lease, CancellationToken cancellationToken)
    {
        var contentStreamId = lease.ContentStreamId;
        if (contentStreamId.IsNullOrEmpty())
            return lease.ToModel();

        var backlog = await StreamingBackend
            .GetSpeechBacklog(StreamId.Parse(contentStreamId), cancellationToken)
            .ConfigureAwait(false);
        return lease.ToModel(backlog);
    }

    private ExpiringEntry<Symbol, Lease> GetOwnLease(StreamId streamId, UserId userId)
    {
        if (!_leases.TryGetValue(streamId.Value, out var expiringLease))
            throw StandardError.NotFound<ChatEntryStream>("This entry stream is unknown or has expired.");
        if (expiringLease.Value.OwnerId != userId)
            throw StandardError.Unauthorized("You can write only to your own entry streams.");

        return expiringLease;
    }

    // Nested types

    private sealed class Lease(StreamId id, UserId ownerId) : IDisposable
    {
        public object Lock { get; } = new();
        public StreamId Id { get; } = id;
        public UserId OwnerId { get; } = ownerId;
        public Channel<string> Chunks { get; } = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true });
        public TaskCompletionSource<ChatEntry> EntryCreatedSource { get; } = TaskCompletionSourceExt.New<ChatEntry>();
        public CancellationTokenSource StopTokenSource { get; } = new(Constants.Chat.MaxEntryStreamDuration);

        public Task<ChatEntry> StreamTask { get; set; } = null!;
        public ChatEntryId EntryId { get; set; }
        public string ContentStreamId { get; set; } = "";
        public int Offset { get; set; }
        public bool IsCompleted { get; set; }

        public void Dispose()
        {
            // Only completes the channel - cancelling StopTokenSource here would drop whatever is
            // still buffered. Completing it is what makes the streamer finalize the entry, so an
            // abandoned stream leaves a readable message rather than an empty one.
            Chunks.Writer.TryComplete();
            _ = (StreamTask ?? Task.CompletedTask).ContinueWith(
                (_, state) => ((CancellationTokenSource)state!).DisposeSilently(),
                StopTokenSource,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public ChatEntryStream ToModel(TimeSpan? speechBacklog = null)
        {
            lock (Lock)
                return new ChatEntryStream(Id, EntryId, Offset, IsCompleted, speechBacklog);
        }
    }
}
