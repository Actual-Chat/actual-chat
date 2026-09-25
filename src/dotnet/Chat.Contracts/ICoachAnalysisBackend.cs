using ActualChat.Time;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Chat;

public interface ICoachAnalysisBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<CoachEntryAnalysis?> Get(ChatEntryId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<CoachConversationAnalysis?> GetConversation(ConversationId id, AuthorId authorId, CancellationToken cancellationToken);
    // lidTileRange must be a Constants.Chat.EntryIdTiles tile: writes invalidate per tile
    [ComputeMethod]
    Task<ApiArray<CoachEntryMarks>> ListMarks(ChatId chatId, AuthorId authorId, Range<long> lidTileRange, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnAnalyzeEntry(CoachAnalysisBackend_AnalyzeEntry command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnAnalyzeConversation(CoachAnalysisBackend_AnalyzeConversation command, CancellationToken cancellationToken);

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record CoachAnalysisBackend_AnalyzeEntry(
    [property: DataMember, Key(0)] ChatEntryId Id,
    [property: DataMember, Key(1)] bool IsRemoved
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Id.ChatId.ShardKey;
}

/// <summary>
/// Analyses the conversation containing <see cref="EntryLid"/> once it has been quiet long enough;
/// the uuid dedupes the burst of commands one conversation produces while it is still being spoken.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record CoachAnalysisBackend_AnalyzeConversation(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long EntryLid
) : ICommand<Unit>, IBackendCommand, IHasShardKey, IHasDelayUntil, IHasUuid, IHasTimeout
{
    private const long LidBucket = 64;

    [DataMember, Key(2)]
    public Moment DelayUntil { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
    string IHasUuid.Uuid => $"coach:{ChatId}:{EntryLid / LidBucket}";
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);
}
