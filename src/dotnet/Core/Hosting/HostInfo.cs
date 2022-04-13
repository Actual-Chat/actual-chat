using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public record HostInfo
{
    private bool? _isProductionInstance;
    private bool? _isStagingInstance;
    private bool? _isDevelopmentInstance;

    public static Symbol ProductionEnvironment { get; } = Environments.Production;
    public static Symbol StagingEnvironment { get; } = Environments.Staging;
    public static Symbol DevelopmentEnvironment { get; } = Environments.Development;

    public Symbol HostKind { get; init; } = Symbol.Empty;
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public ImmutableHashSet<Symbol> RequiredServiceScopes { get; init; } = ImmutableHashSet<Symbol>.Empty;

    public bool IsProductionInstance => _isProductionInstance ??= Environment == ProductionEnvironment;
    public bool IsStagingInstance => _isStagingInstance ??= Environment == StagingEnvironment;
    public bool IsDevelopmentInstance => _isDevelopmentInstance ??= Environment == DevelopmentEnvironment;
}
