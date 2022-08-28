using Newtonsoft.Json;

namespace ActualChat.Kubernetes.Api;

public record Labels(
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string App,
    [property: JsonProperty("kubernetes.io/service-name")]
    string ServiceName
);
