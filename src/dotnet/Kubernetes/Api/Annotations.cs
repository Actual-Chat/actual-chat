 namespace ActualChat.Kubernetes.Api;

public sealed class Annotations : Dictionary<string, string>
{
    public Annotations() { }
    public Annotations(IEnumerable<KeyValuePair<string, string>> collection) : base(collection) { }
}
