using ActualChat.Sharding;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for translating chat entry content between languages.
/// </summary>
public interface ITranslationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Translation?> Get(TranslationId id, bool translateIfMissing, CancellationToken cancellationToken);

    // Non-compute methods

    Task<ApiArray<Translation>> ListHanging(ThisNodeRef nodeRef, int limit, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task<Translation?> OnChange(TranslationsBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Translation?> OnTranslate(TranslationsBackend_Translate command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<StreamId?> OnTranslateStream(TranslationsBackend_TranslateStream command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a translation record.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TranslationsBackend_Change(
    [property: DataMember, Key(0)] TranslationId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<TranslationDiff> Change) : ICommand<Translation>, IBackendCommand, IHasShardKey<TranslationSourceId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public TranslationSourceId ShardKey => Id.SourceId;
}

/// <summary>
/// Command to translate content to a target language.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TranslationsBackend_Translate(
    [property: DataMember, Key(0)] TranslationSourceId SourceId,
    [property: DataMember, Key(1)] Language TargetLanguage,
    [property: DataMember, Key(2)] bool OverwriteIfVersionMismatch,
    [property: DataMember, Key(3)] bool SkipRealtimeTranslation
) : ICommand<Translation?>, IBackendCommand, IHasShardKey<TranslationSourceId>, IHasUuid, IHasTimeout
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public TranslationSourceId ShardKey => SourceId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public TimeSpan? Timeout => TimeSpan.FromSeconds(180);

    string IHasUuid.Uuid => $"Translate:{SourceId.Value}:{TargetLanguage.Value}";
}

/// <summary>
/// Command to translate an audio stream to a target language in real-time.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TranslationsBackend_TranslateStream(
    [property: DataMember, Key(0)] StreamId Id,
    [property: DataMember, Key(1)] Language TargetLanguage
) : ICommand<StreamId?>, IBackendCommand, IHasShardKey<StreamId>, IHasUuid
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public StreamId ShardKey => Id;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public StreamId TargetStreamId { get; } = StreamId.New(Id, TargetLanguage);

    string IHasUuid.Uuid => TargetStreamId.Value;
}
