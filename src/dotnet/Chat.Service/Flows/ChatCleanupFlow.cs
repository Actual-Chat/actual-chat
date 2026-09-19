using ActualChat.Chat.Module;
using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

[Flow(DelayQuanta = 10, ResumeTimeout = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class ChatCleanupFlow : Flow<Unit>
{
    private ICommander Commander => field ??= Services.Commander();
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(Id.Arguments);
        var count = await Commander.Call(new ChatsBackend_Cleanup(chatId), true, cancellationToken)
            .ConfigureAwait(false);
        if (count < 0)
            return;

        if (count >= Settings.CleanupBatchSize)
            Runtime.StageResume();
        else
            Runtime.StageResumeIn(Settings.CleanupInterval);
    }
}
