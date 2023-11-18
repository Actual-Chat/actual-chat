namespace ActualChat.Kubernetes.Api;

#pragma warning disable CA1720 // Object contains type name

public record Change<T>(ChangeType Type, T Object);

// ReSharper disable InconsistentNaming
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeType
{
    Added,
    Modified,
    Deleted,
}
