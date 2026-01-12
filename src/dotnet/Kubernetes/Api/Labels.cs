using System.Text.Json.Serialization;

namespace ActualChat.Kubernetes.Api;

public class Labels : Dictionary<string, string>
{
    public Labels() { }
    public Labels(IEnumerable<KeyValuePair<string, string>> collection) : base(collection) { }

    [JsonIgnore]
    public string? App {
        get => TryGetValue(nameof(App), out var value) ? value : null;
        set {
            if (value != null) this[nameof(App)] = value;
            else Remove(nameof(App));
        }
    }

    [JsonIgnore]
    public string? ServiceName {
        get => TryGetValue("kubernetes.io/service-name", out var value) ? value : null;
        set {
            if (value != null) this["kubernetes.io/service-name"] = value;
            else Remove("kubernetes.io/service-name");
        }
    }
}
