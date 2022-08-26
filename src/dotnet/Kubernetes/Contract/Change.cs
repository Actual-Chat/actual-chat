namespace ActualChat.Kubernetes.Contract;

public record Change<T>(ChangeType Type, T Object);

// ReSharper disable InconsistentNaming
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeType
{
    Added,
    Modified,
    Deleted,
}
