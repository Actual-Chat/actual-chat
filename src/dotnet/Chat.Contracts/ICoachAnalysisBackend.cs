using ActualChat.Time;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Chat;

public interface ICoachAnalysisBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<CoachEntryAnalysis?> Get(ChatEntryId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<CoachConversationAnalysis?> GetConversation(
        ConversationId id, AuthorId authorId, CancellationToken cancellationToken);
    // lidTileRange must be a Constants.Chat.EntryIdTiles tile: writes invalidate per tile
    [ComputeMethod]
    Task<ApiArray<CoachEntryMarks>> ListMarks(
        ChatId chatId, AuthorId authorId, Range<long> lidTileRange, CancellationToken cancellationToken);

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
) : ICommand<Unit>, IBackendCommand, IHasShardKey, IHasTimeout
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Id.ChatId.ShardKey;
    // The immediate path calls the LLM from this command; the default queue budget is 15 s
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(3);
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
    // Distinguishes a re-analysis (an edit) from the burst of commands a fresh run produces
    [DataMember, Key(3)]
    public string Salt { get; init; } = "";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
    string IHasUuid.Uuid => Salt.IsNullOrEmpty()
        ? $"coach:{ChatId}:{EntryLid / LidBucket}"
        : $"coach:{ChatId}:{EntryLid / LidBucket}:{Salt}";
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);
}
