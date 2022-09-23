namespace ActualChat.Kubernetes.Api;

public record Change<T>(ChangeType Type, T Object);

// ReSharper disable InconsistentNaming
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeType
{
    Added,
    Modified,
    Deleted,
}
