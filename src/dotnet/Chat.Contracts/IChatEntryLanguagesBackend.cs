using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IChatEntryLanguagesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ChatEntryLanguage?> GetLanguage(ChatEntryId id, CancellationToken cancellationToken);

    // Non-compute methods

    Task<ChatEntryLanguage[]> ListForDetection(int limit, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task<Result<ChatEntryLanguage?>[]> OnBulkChange(ChatEntryLanguagesBackend_BulkChange command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntryLanguage?> OnReset(ChatEntryLanguagesBackend_Reset command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Result<ChatEntryLanguage?>> OnTryChange(ChatEntryLanguagesBackend_TryChange command, CancellationToken cancellationToken);

    // Event handlers

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);

    [EventHandler]
    Task OnChatEntryLanguagesChangedEvent(ChatEntryLanguagesChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatEntryLanguagesBackend_BulkChange(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryLanguageChange[] Changes
) : ICommand<Result<ChatEntryLanguage?>[]>, IBackendCommand, IHasShardKey<ChatEntryId>
{
    public static ChatEntryLanguagesBackend_BulkChange Upserts(params IEnumerable<ChatEntryLanguage> languages)
        => new(languages.Select(x => new ChatEntryLanguageChange(x.Id, x.Version, Change.Upsert(x))).ToArray());

    [IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId ShardKey => Changes[0].Id;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatEntryLanguagesBackend_Reset(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id
) : ICommand<ChatEntryLanguage?>, IBackendCommand, IHasShardKey<ChatEntryId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId ShardKey => Id;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatEntryLanguagesBackend_TryChange(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryLanguageChange Change
) : ICommand<Result<ChatEntryLanguage?>>, IBackendCommand, IHasShardKey<ChatEntryId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId ShardKey => Change.Id;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatEntryLanguageChange(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatEntryLanguage> Change
);
