using MemoryPack;

namespace ActualChat.Chat;

public interface IChatsUpgradeBackend : ICommandService
{
    [CommandHandler]
    Task<Chat> OnCreateDefaultChat(ChatsUpgradeBackend_CreateDefaultChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> OnCreateAnnouncementsChat(ChatsUpgradeBackend_CreateAnnouncementsChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> OnCreateFeedbackTemplateChat(ChatsUpgradeBackend_CreateFeedbackTemplateChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpgradeChat(ChatsUpgradeBackend_UpgradeChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnFixCorruptedReadPositions(ChatsUpgradeBackend_FixCorruptedReadPositions command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateDefaultChat(
) : ICommand<Chat>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateAnnouncementsChat(
) : ICommand<Chat>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateFeedbackTemplateChat(
) : ICommand<Chat>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_FixCorruptedReadPositions(
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_UpgradeChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId
) : ICommand<Unit>, IBackendCommand;
