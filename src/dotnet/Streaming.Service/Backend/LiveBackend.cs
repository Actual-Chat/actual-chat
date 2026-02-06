using ActualChat.Chat;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service implementation for managing active live audio streams in chats.
/// </summary>
public partial class LiveBackend : ShardComputeService, ILiveBackend
{
    private readonly ConcurrentDictionary<ChatId, ChatState> _chatStates = new();

    internal IChatsBackend ChatsBackend { get; }
    internal MomentClock ServerClock { get; }

    public LiveBackend(IServiceProvider services)
        : base(services, ShardScheme.AudioBackend)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        ServerClock = services.Clocks().ServerClock;

        var stopToken = ShardOwner.StopToken;
        foreach (var shardIndex in ShardScheme.ShardIndexes) {
            var shardIndexCopy = shardIndex; // Capture for closure
            var shardState = ShardOwner.States[shardIndex].Value;
            // Clear streams only when THIS shard loses ownership
            _ = Task.Run(async () => {
                while (true) {
                    shardState = await shardState.WhenNext(stopToken).ConfigureAwait(false);
                    // Only clear when we lose ownership (not when gaining or during startup)
                    if (shardState.OwnershipStatus == ShardOwnershipStatus.MappedToOtherNode)
                        PurgeShard(shardIndexCopy);
                }
            }, CancellationToken.None);
        }
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        return await chatState.ListActiveStreams(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<RpcStream<LiveStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        // linkedCts will be either cancelled (most likely) or GC collected - that's why we don't dispose it explicitly
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var chatState = GetChatState(chatId);
        var observations = chatState.ObserveStreams(linkedCts.Token);
        return RpcStream.New(observations, isReconnectable: false);
    }

    public virtual async Task RegisterActiveStream(ChatId chatId, LiveStreamInfo activeStream, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        if (await chatState.Register(activeStream, cancellationToken).ConfigureAwait(false))
            InvalidateListActiveStreams(chatId);
    }

    public virtual async Task UnregisterActiveStream(ChatId chatId, string streamId, CancellationToken cancellationToken)
    {
        if (!_chatStates.TryGetValue(chatId, out var chatState))
            return;
        if (await chatState.Unregister(streamId, cancellationToken).ConfigureAwait(false))
            InvalidateListActiveStreams(chatId);
    }

    // Private methods

    private ChatState GetChatState(ChatId chatId)
        => _chatStates.GetOrAdd(chatId, static (id, self) => new ChatState(self, id), this);

    private void PurgeShard(int shardIndex)
    {
        // Only clear streams for chats that belong to this shard
        var chatIdsToRemove = _chatStates.Keys
            .Where(chatId => ShardScheme.GetShardIndex(chatId) == shardIndex)
            .ToList();

        foreach (var chatId in chatIdsToRemove) {
            if (!_chatStates.TryRemove(chatId, out var chatState))
                continue;

            InvalidateListActiveStreams(chatId);
            chatState.Complete(RpcRerouteException.MustReroute());
        }
    }

    private void InvalidateListActiveStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListActiveStreams(chatId, default);
    }
}
