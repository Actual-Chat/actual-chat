using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Flows.Db;

// The whole inbox of a flow in one row, read and replaced as a unit under a row lock. Every change
// rewrites it, so it suits inboxes that are drained regularly. The row outlives an empty inbox to
// keep LastId, so a message Id is never reused while the flow exists.

[Table("_FlowInboxes")]
public sealed class DbFlowInbox
{
    private static readonly Type MessagesType = typeof(ApiArray<FlowInboxMessage>);

    [DbKey]
    public string Id { get; set; } = "";
    [ConcurrencyCheck]
    public long Version { get; set; }
    public long LastId { get; set; }
    public byte[] Data { get; set; } = [];

    public ApiArray<FlowInboxMessage> GetMessages()
        => Data.Length == 0
            ? []
            : (ApiArray<FlowInboxMessage>)Serializers.MessagePack.Read(Data, MessagesType, out _)!;

    public void SetMessages(ApiArray<FlowInboxMessage> messages)
    {
        if (messages.IsEmpty) {
            Data = [];
            return;
        }

        using var buffer = new ArrayPoolBuffer<byte>(4096, false);
        Serializers.MessagePack.Write(buffer, messages, MessagesType);
        Data = buffer.WrittenSpan.ToArray();
    }
}
