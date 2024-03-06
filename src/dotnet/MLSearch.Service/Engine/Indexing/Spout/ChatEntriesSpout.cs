
namespace ActualChat.MLSearch.Engine.Indexing.Spout;


// Note: solution drawback
// It has a chance that the receiving counter part will fail to complete
// event handling while the event will be marked as complete.
// This means: At most once logic.
internal class ChatEntriesSpout : IChatEntriesSpout, IComputeService//, ICommandHandler<MLSearch_TriggerContinueChatIndexing>
{
    private readonly ChannelWriter<MLSearch_TriggerContinueChatIndexing> _send;
    public ChatEntriesSpout(IServiceProvider serviceProvider)
    {
        // TODO: fix. Testing if this helps with fusion.AddService to make it working.
        _send = serviceProvider.GetRequiredService<ChatEntriesIndexer>().Trigger;
    }

    // ReSharper disable once UnusedMember.Global
    public virtual async Task OnCommand(MLSearch_TriggerContinueChatIndexing e, CancellationToken cancellationToken)
        => await _send.WriteAsync(e, cancellationToken).ConfigureAwait(false);
}
