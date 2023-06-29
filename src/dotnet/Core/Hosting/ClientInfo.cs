namespace ActualChat.Hosting;

public record ClientInfo(Symbol TenantId, string? UserAgent, string? IpAddress)
{
    public static readonly ClientInfo Default = new (Symbol.Empty, null, null);
}
