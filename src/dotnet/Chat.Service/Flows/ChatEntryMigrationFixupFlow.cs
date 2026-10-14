using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Retired along with <see cref="ChatEntryMigrationFlow"/>; kept so that the persisted flow state still resolves.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class ChatEntryMigrationFixupFlow : ChatEntryMigrationFlow;
