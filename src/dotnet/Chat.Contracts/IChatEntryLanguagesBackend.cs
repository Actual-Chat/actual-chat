using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IChatEntryLanguagesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ChatEntryLanguage?> GetLanguage(ChatEntryId id, CancellationToken cancellationToken);

    // Non-compute methods
    Task<ApiArray<ChatEntryLanguage>> ListForDetection(int limit, CancellationToken cancellationToken);

    // Command handlers
    [CommandHandler]
    Task<ApiArray<Result<ChatEntryLanguage?>>> OnBulkChange(ChatEntryLanguagesBackend_BulkChange command, CancellationToken cancellationToken);

    // Event handlers
    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatEntryLanguagesBackend_BulkChange(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<ChatEntryLanguageChange> Changes
) : ICommand<ApiArray<Result<ChatEntryLanguage?>>>, IBackendCommand
{
    public static ChatEntryLanguagesBackend_BulkChange Upserts(params IEnumerable<ChatEntryLanguage> languages)
        => new(languages.Select(x => new ChatEntryLanguageChange(x.Id, x.Version, Change.Upsert(x))).ToApiArray());
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatEntryLanguageChange(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatEntryLanguage> Change
);
