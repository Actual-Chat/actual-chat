using System.Text.Json.Serialization;

namespace ActualChat.Kubernetes.Api;

public class Annotations : Dictionary<string, string>
{
    public Annotations() { }
    public Annotations(IEnumerable<KeyValuePair<string, string>> collection) : base(collection) { }
}
