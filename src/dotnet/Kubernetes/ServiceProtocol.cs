namespace ActualChat.Kubernetes;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceProtocol
{
    TCP = 0,
    UDP,
    SCTP,
}
