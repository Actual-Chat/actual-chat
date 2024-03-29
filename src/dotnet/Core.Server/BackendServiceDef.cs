using ActualChat.Hosting;

namespace ActualChat;

public sealed record BackendServiceDef(
    Type ServiceType,
    Type ImplementationType,
    ServiceMode ServiceMode,
    HostRole? HostedByRole)
{
    private string? _toStringCached;

    public ShardScheme ShardScheme { get; }
        = HostedByRole is { } hostedByRole
            ? ShardScheme.ById.GetValueOrDefault(hostedByRole, ShardScheme.None)
            : ShardScheme.None;

    public override string ToString()
    {
        if (_toStringCached != null)
            return _toStringCached;

        var prefix = ImplementationType == ServiceType
            ? ServiceType.GetName()
            : $"{ServiceType.GetName()} -> {ImplementationType.GetName()}";
        return _toStringCached =
            $"{prefix} as {ServiceMode:G}, hosted by {HostedByRole ?? "None"} * {ShardScheme}";
    }

    // This record relies on referential equality
    public bool Equals(BackendServiceDef? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
};
