using ActualChat.Hashing;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IChatEntryLanguagesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ChatEntryLanguage?> Get(ChatEntryId id, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task<ChatEntryLanguage?> OnDetect(ChatEntryLanguagesBackend_Detect command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntryLanguage?> OnChange(ChatEntryLanguagesBackend_Change command, CancellationToken cancellationToken);

    // Event handlers

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatEntryLanguagesBackend_Detect(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember, MemoryPackOrder(1)] HashString ContentHash
) : ICommand<ChatEntryLanguage?>, IBackendCommand, IHasShardKey<ChatId>, IHasUuid
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Id.ChatId;

    string IHasUuid.Uuid => $"{Id}.{ContentHash.Hash}";
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatEntryLanguagesBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatEntryLanguage> Change
) : ICommand<ChatEntryLanguage?>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Id.ChatId;

    public static ChatEntryLanguagesBackend_Change Upsert(ChatEntryLanguage language)
        => new (language.Id, language.Version, ActualChat.Change.Upsert(language));

    public static ChatEntryLanguagesBackend_Change Remove(ChatEntryLanguage language)
        => new (language.Id, language.Version, ActualChat.Change.Remove(language));

    public static ChatEntryLanguagesBackend_Change Remove(ChatEntryId id)
        => new (id, null, ActualChat.Change.Remove<ChatEntryLanguage>());
}
