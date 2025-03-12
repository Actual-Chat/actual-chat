using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class LanguageDetectionMasterFlow
    : IndexingMasterFlowBase<LanguageDetectionFlow, Chat, ChatId>, IMasterFlow
{
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Host.Services.GetRequiredService<IChatsBackend>();

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public long MaxVersion { get; private set; }

    protected override int CurrentFlowSetVersion => 5;
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Host.Services.GetRequiredService<ChatSettings>();

    protected override async Task<bool> OnBeforeFirstIndexAfterReset(CancellationToken cancellationToken)
    {
        var mustContinue = await base.OnBeforeFirstIndexAfterReset(cancellationToken).ConfigureAwait(false);
        if (mustContinue)
            // only created before now + 10sec. New chats are handled from events
            // Note: intentionally set negative number
            MaxVersion = Clocks.GetMaxVersion(TimeSpan.FromSeconds(-10));

        return mustContinue;
    }

    protected override async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
        {
            Log.LogInformation("`{Id}`.OnBeforeFirstIndexAfterReset: translation is disabled, flow will not start", Id);
            return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt);
        }

        return await base.OnIndex(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IReadOnlyList<Chat>> GetBatch(IndexingFlowCursor<ChatId>? cursor, CancellationToken cancellationToken)
    {
        cursor ??= new (ChatId.None, 0);
        return await ChatsBackend.ListChanged(new ChangedChatsQuery {
                    MinVersion = cursor.LastUpdatedVersion,
                    MaxVersion = MaxVersion,
                    LastId = cursor.LastUpdatedId,
                    Limit = BatchSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
