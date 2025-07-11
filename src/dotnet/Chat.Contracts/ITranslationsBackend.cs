using ActualChat.Hashing;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface ITranslationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Translation?> Get(TranslationId id, CancellationToken cancellationToken);

    // Not a compute method
    Task<string> Translate(TranslationId id, string prefix, string content, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task<Translation?> OnChange(TranslationsBackend_Change command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Translation?> OnTranslate(TranslationsBackend_Translate command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record TranslationsBackend_Change(
    TranslationId Id,
    long? ExpectedVersion,
    Change<Translation> Change) : ChangeCommand<Translation, TranslationId>(Id, ExpectedVersion, Change);

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record TranslationsBackend_Translate(
    [property: DataMember, MemoryPackOrder(0)] TranslationId Id,
    [property: DataMember, MemoryPackOrder(1)] bool OverwriteIfVersionMismatch
) : ICommand<Translation?>, IBackendCommand, IHasShardKey<TranslationId>, IHasUuid
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public TranslationId ShardKey => Id;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    string IHasUuid.Uuid => Id.Value;
}
