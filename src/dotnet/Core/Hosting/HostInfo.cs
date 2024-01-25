using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public sealed record HostInfo
{
    private readonly BoolOption _isProductionInstance = new();
    private readonly BoolOption _isStagingInstance = new();
    private readonly BoolOption _isDevelopmentInstance = new();

    public static readonly Symbol ProductionEnvironment = Environments.Production;
    public static readonly Symbol StagingEnvironment = Environments.Staging;
    public static readonly Symbol DevelopmentEnvironment = Environments.Development;

    public HostKind HostKind { get; init; }
    public AppKind AppKind { get; init; }
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public string DeviceModel { get; init; } = "Unknown";
    public IReadOnlySet<HostRole> Roles { get; init; } = ImmutableHashSet<HostRole>.Empty;
    public bool IsTested { get; init; }
    public string BaseUrl { get; init; } = "";

    public bool IsProductionInstance => _isProductionInstance.Value ??= Environment == ProductionEnvironment;
    public bool IsStagingInstance => _isStagingInstance.Value ??= Environment == StagingEnvironment;
    public bool IsDevelopmentInstance => _isDevelopmentInstance.Value ??= Environment == DevelopmentEnvironment;

    public bool HasRole(HostRole role) => Roles.Contains(role);

    public ServiceMode GetServiceMode(HostRole role)
        => HasRole(HostRole.SingleServer)
            ? ServiceMode.SelfHosted
            : HasRole(role)
                ? ServiceMode.Server
                : ServiceMode.Client;
}
