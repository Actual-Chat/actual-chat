namespace ActualChat.Kubernetes.Contract;

public record Endpoint(
    IReadOnlyList<string> Addresses,
    Conditions Conditions,
    TargetRef TargetRef,
    string NodeName,
    string Zone
);

public record Conditions(
    bool Ready,
    bool Serving,
    bool Terminating
);

public record TargetRef(
    string Kind,
    string Namespace,
    string Name,
    string Uid,
    string ResourceVersion
);

