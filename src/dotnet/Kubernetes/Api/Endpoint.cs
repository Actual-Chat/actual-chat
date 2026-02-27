namespace ActualChat.Kubernetes.Api;

public sealed record Endpoint(
    IReadOnlyList<string> Addresses,
    Conditions Conditions,
    TargetRef TargetRef,
    string NodeName,
    string Zone
);

public sealed record Conditions(
    bool Ready,
    bool Serving,
    bool Terminating
);

public sealed record TargetRef(
    string Kind,
    string Namespace,
    string Name,
    string Uid,
    string ResourceVersion
);
