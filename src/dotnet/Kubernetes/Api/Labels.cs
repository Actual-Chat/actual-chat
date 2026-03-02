namespace ActualChat.Kubernetes.Api;

public class Labels : Dictionary<string, string>
{
    public const string KubernetesIoServiceName = "kubernetes.io/service-name";

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
        get => TryGetValue(KubernetesIoServiceName, out var value) ? value : null;
        set {
            if (value != null) this[KubernetesIoServiceName] = value;
            else Remove(KubernetesIoServiceName);
        }
    }
}
