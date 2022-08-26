using Newtonsoft.Json;

namespace ActualChat.Kubernetes.Contract;

public record Metadata(
    string ResourceVersion,
    string Name,
    string GenerateName,
    string Namespace,
    string Uid,
    int Generation,
    DateTime CreationTimestamp,
    Labels Labels,
    IReadOnlyList<OwnerReference> OwnerReferences
);

public record Labels(
    string App,
    [property: JsonProperty("kubernetes.io/service-name")]
    string ServiceName
);
