using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public sealed record HostInfo
{
    private LazySlim<BaseUrlKind>? _baseUrlKind;
    private LazySlim<bool>? _isProductionInstance;
    private LazySlim<bool>? _isDevelopmentInstance;

    public HostKind HostKind { get; init; }
    public AppKind AppKind { get; init; }
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public string DeviceModel { get; init; } = "Unknown";
    public IReadOnlySet<HostRole> Roles { get; init; } = ImmutableHashSet<HostRole>.Empty;
    public bool IsTested { get; init; }
    public string BaseUrl { get; init; } = "";

    // Computed & cached
    public BaseUrlKind BaseUrlKind => (_baseUrlKind ??= LazySlim.New(GetBaseUrlKind(BaseUrl))).Value;
    public bool IsProductionInstance => (_isProductionInstance ??= LazySlim.New(IsProductionEnv())).Value;
    public bool IsDevelopmentInstance => (_isDevelopmentInstance ??= LazySlim.New(IsDevelopmentEnv())).Value;

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
            || (!IsTested && HostKind.IsServer() && BaseUrlKind == BaseUrlKind.Production); // We don't want to mess it up

    private bool IsDevelopmentEnv()
        => !IsProductionEnv() && OrdinalEquals(Environment.Value, Environments.Development);
}
