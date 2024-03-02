using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public sealed record HostInfo
{
    private Mutable<BaseUrlKind>? _baseUrlKind;
    private Mutable<bool>? _isProductionInstance;
    private Mutable<bool>? _isDevelopmentInstance;

    public HostKind HostKind { get; init; }
    public AppKind AppKind { get; init; }
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public string DeviceModel { get; init; } = "Unknown";
    public IReadOnlySet<HostRole> Roles { get; init; } = ImmutableHashSet<HostRole>.Empty;
    public bool IsTested { get; init; }
    public string BaseUrl { get; init; } = "";

    // Computed & cached
    public BaseUrlKind BaseUrlKind => (_baseUrlKind ??= Mutable.New(GetBaseUrlKind(BaseUrl))).Value;
    public bool IsProductionInstance => (_isProductionInstance ??= Mutable.New(IsProductionEnv())).Value;
    public bool IsDevelopmentInstance => (_isDevelopmentInstance ??= Mutable.New(IsDevelopmentEnv())).Value;

    public bool HasRole(HostRole role) => Roles.Contains(role);

    // Private methods

    private static BaseUrlKind GetBaseUrlKind(string baseUrl)
    {
        var host = baseUrl.EnsureSuffix("/").ToUri().Host;
        return OrdinalIgnoreCaseEquals(host, "actual.chat") ? BaseUrlKind.Production
            : OrdinalIgnoreCaseEquals(host, "dev.actual.chat") ? BaseUrlKind.Development
            : OrdinalIgnoreCaseEquals(host, "local.actual.chat") ? BaseUrlKind.Local
            : BaseUrlKind.Unknown;
    }

    private bool IsProductionEnv()
        => OrdinalEquals(Environment.Value, Environments.Production)
            || (HostKind.IsServer() && BaseUrlKind == BaseUrlKind.Production); // We don't want to mess it up

    private bool IsDevelopmentEnv()
        => !IsProductionEnv() && OrdinalEquals(Environment.Value, Environments.Development);
}
