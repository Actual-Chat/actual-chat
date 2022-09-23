namespace ActualChat.Kubernetes.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceProtocol
{
    // ReSharper disable once InconsistentNaming
    TCP = 0,
    // ReSharper disable once InconsistentNaming
    UDP = 1,
    // ReSharper disable once InconsistentNaming
    SCTP = 2,
}
