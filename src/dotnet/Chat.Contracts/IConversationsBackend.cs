using ActualChat.Time;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing conversations (chat entry groupings with AI summaries).
/// </summary>
public interface IConversationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Conversation[]> GetTile(
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ConversationRangeMeta> GetRangeMeta(ChatId chatId, long idTileStart, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Conversation> OnChange(ConversationBackend_Change command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Conversation> OnSummarize(ConversationBackend_Summarize command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Conversation> OnMaterialize(ConversationBackend_Materialize command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Conversation?> OnAppendReply(ConversationBackend_AppendReply command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a conversation.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Change(
    [property: DataMember, Key(0)] ConversationId ConversationId,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<ConversationDiff> Change
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>
{
    [DataMember, Key(3)]
    public bool IsLiveMaterialization { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ConversationId.ChatId;
}

/// <summary>
/// Command to generate an AI summary for a conversation.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Summarize(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] Range<long>[] EntryLidRanges
    ) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IHasDelayUntil, IHasTimeout
{
    [DataMember, Key(2)]
    public Moment DelayUntil { get; init; }
    [DataMember, Key(3)]
    public bool IsLiveMaterialization { get; init; }

    ChatId IHasShardKey<ChatId>.ShardKey => ChatId;
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);

    public override string ToString()
        => $"ConversationBackend_Summarize {{ ChatId={ChatId}, EntryLidRanges=[{string.Join(", ", EntryLidRanges.Select(r => r.Format()))}], DelayUntil={DelayUntil} }}";
}

/// <summary>
/// Command to persist a pre-computed (live-session) conversation as-is, without re-running the summarizer.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Materialize(
    [property: DataMember, Key(0)] Conversation Conversation
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Conversation.Id.ChatId;
}

/// <summary>
/// Command to append a reply entry range to a conversation.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_AppendReply(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long EntryLid,
    [property: DataMember, Key(2)] Range<long> ReplyLidRange
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IHasDelayUntil, IHasTimeout
{
    [DataMember, Key(3)]
    public Moment DelayUntil { get; init; }

    ChatId IHasShardKey<ChatId>.ShardKey => ChatId;
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);
}
