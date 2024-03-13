using ActualChat.MLSearch.ApiAdapters;

namespace ActualChat.MLSearch.Bot;

internal interface IChatBotWorker
{
    ChannelWriter<MLSearch_TriggerContinueConversationWithBot> Trigger { get; }
    Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken);
}