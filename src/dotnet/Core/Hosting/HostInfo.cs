using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public record HostInfo
{
    private bool? _isProductionInstance;
    private bool? _isStagingInstance;
    private bool? _isDevelopmentInstance;

    public static readonly Symbol ProductionEnvironment = Environments.Production;
    public static readonly Symbol StagingEnvironment = Environments.Staging;
    public static readonly Symbol DevelopmentEnvironment = Environments.Development;

    public Symbol HostKind { get; init; } = Symbol.Empty;
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public ImmutableHashSet<Symbol> RequiredServiceScopes { get; init; } = ImmutableHashSet<Symbol>.Empty;

    public bool IsProductionInstance => _isProductionInstance ??= Environment == ProductionEnvironment;
    public bool IsStagingInstance => _isStagingInstance ??= Environment == StagingEnvironment;
    public bool IsDevelopmentInstance => _isDevelopmentInstance ??= Environment == DevelopmentEnvironment;
}
