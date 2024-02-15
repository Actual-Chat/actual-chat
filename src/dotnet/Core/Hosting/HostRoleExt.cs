namespace ActualChat.Hosting;

public static class HostRoleExt
{
    public static HostRole RequireBackend(this HostRole hostRole)
        => hostRole.IsBackendServer
            ? hostRole
            : throw StandardError.Constraint($"Backend HostRole is required, but found {hostRole.Id}.");
}
