using ActualChat.Hashing;
using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for detecting and storing chat entry languages.
/// </summary>
public interface IChatEntryLanguagesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ChatLanguageTile> GetTile(
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task<ChatEntryLanguage?> OnDetect(ChatEntryLanguagesBackend_Detect command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntryLanguage?> OnChange(ChatEntryLanguagesBackend_Change command, CancellationToken cancellationToken);

    // Event handlers

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to detect the language of a chat entry.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatEntryLanguagesBackend_Detect(
    [property: DataMember, Key(0)] ChatEntryId Id,
    [property: DataMember, Key(1)] HashString ContentHash
) : ICommand<ChatEntryLanguage?>, IBackendCommand, IHasShardKey<ChatId>, IHasUuid
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Id.ChatId;

    string IHasUuid.Uuid => $"{Id}.{ContentHash.Hash}";
}

/// <summary>
/// Command to update the detected language for a chat entry.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatEntryLanguagesBackend_Change(
    [property: DataMember, Key(0)] ChatEntryId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<ChatEntryLanguage> Change
) : ICommand<ChatEntryLanguage?>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Id.ChatId;

    public static ChatEntryLanguagesBackend_Change Upsert(ChatEntryLanguage language)
        => new (language.Id, language.Version, ActualChat.Change.Upsert(language));

    public static ChatEntryLanguagesBackend_Change Remove(ChatEntryLanguage language)
        => new (language.Id, language.Version, ActualChat.Change.Remove(language));

    public static ChatEntryLanguagesBackend_Change Remove(ChatEntryId id)
        => new (id, null, ActualChat.Change.Remove<ChatEntryLanguage>());
}
