using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface ITranslationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Translation?> Get(TranslationId id, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task<Translation?> OnChange(TranslationsBackend_Change command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Translation?> OnTranslate(TranslationsBackend_Translate command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<StreamId?> OnTranslateStream(TranslationsBackend_TranslateStream command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record TranslationsBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] TranslationId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<TranslationDiff> Change) : ICommand<Translation>, IBackendCommand, IHasShardKey<TranslationSourceId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public TranslationSourceId ShardKey => Id.SourceId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record TranslationsBackend_Translate(
    [property: DataMember, MemoryPackOrder(0)] TranslationSourceId SourceId,
    [property: DataMember, MemoryPackOrder(1)] Language TargetLanguage,
    [property: DataMember, MemoryPackOrder(2)] bool OverwriteIfVersionMismatch,
    [property: DataMember, MemoryPackOrder(3)] bool SkipRealtimeTranslation
) : ICommand<Translation?>, IBackendCommand, IHasShardKey<TranslationSourceId>, IHasUuid
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public TranslationSourceId ShardKey => SourceId;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    string IHasUuid.Uuid => "Translate:" + SourceId.Value;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record TranslationsBackend_TranslateStream(
    [property: DataMember, MemoryPackOrder(0)] StreamId Id,
    [property: DataMember, MemoryPackOrder(1)] Language TargetLanguage
) : ICommand<StreamId?>, IBackendCommand, IHasShardKey<StreamId>, IHasUuid
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public StreamId ShardKey => Id;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    string IHasUuid.Uuid => TargetStreamId.Value;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public StreamId TargetStreamId { get; } = StreamId.New(Id, TargetLanguage);
}
