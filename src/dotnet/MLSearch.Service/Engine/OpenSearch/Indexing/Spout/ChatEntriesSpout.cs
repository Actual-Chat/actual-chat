
namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing.Spout;


// Note: solution drawback
// It has a chance that the receiving counter part will fail to complete
// event handling while the event will be marked as complete.
// This means: At most once logic.
public class ChatEntriesSpout : IChatEntriesSpout, IComputeService//, ICommandHandler<MLSearch_TriggerContinueChatIndexing>
{
    private readonly ChannelWriter<MLSearch_TriggerContinueChatIndexing> send;
    public ChatEntriesSpout(IServiceProvider serviceProvider)
    {
        // TODO: fix. Testing if this helps with fusion.AddService to make it working.
        send = serviceProvider.GetRequiredService<ChatEntriesIndexing>().Trigger;
    }

    // ReSharper disable once UnusedMember.Global
    public virtual async Task OnCommand(MLSearch_TriggerContinueChatIndexing e, CancellationToken cancellationToken)
        => await send.WriteAsync(e, cancellationToken).ConfigureAwait(false);
}
