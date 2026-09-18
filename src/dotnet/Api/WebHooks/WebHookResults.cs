namespace ActualChat.WebHooks;

[DataContract, MessagePackObject]
public sealed partial record WebHookChangeResult(
    [property: DataMember, Key(0)] WebHook? WebHook,
    // Only set on create
    [property: DataMember, Key(1)] string? Secret);

[DataContract, MessagePackObject]
public sealed partial record WebHookTestResult(
    [property: DataMember, Key(0)] bool IsSuccess,
    [property: DataMember, Key(1)] int? StatusCode,
    [property: DataMember, Key(2)] string? Error,
    [property: DataMember, Key(3)] int LatencyMs);
