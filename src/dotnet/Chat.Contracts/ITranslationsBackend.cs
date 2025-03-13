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
    [property: DataMember, MemoryPackOrder(0)] TranslationId Id
) : ICommand<Translation?>, IBackendCommand, IHasShardKey<TranslationId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public TranslationId ShardKey => Id;
}
