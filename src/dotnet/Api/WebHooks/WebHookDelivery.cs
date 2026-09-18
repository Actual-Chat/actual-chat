namespace ActualChat.WebHooks;

[DataContract, MessagePackObject]
public sealed partial record WebHookDelivery(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] WebHookId WebHookId)
{
    [DataMember, Key(2)] public long Seq { get; init; }
    [DataMember, Key(3)] public string EventType { get; init; } = "";
    [DataMember, Key(4)] public WebHookDeliveryStatus Status { get; init; }
    [DataMember, Key(5)] public int Attempts { get; init; }
    [DataMember, Key(6)] public Moment? NextAttemptAt { get; init; }
    [DataMember, Key(7)] public int? LastStatusCode { get; init; }
    [DataMember, Key(8)] public string? LastError { get; init; }
    [DataMember, Key(9)] public int? LastLatencyMs { get; init; }
    [DataMember, Key(10)] public Moment CreatedAt { get; init; }
    [DataMember, Key(11)] public Moment? CompletedAt { get; init; }
}
