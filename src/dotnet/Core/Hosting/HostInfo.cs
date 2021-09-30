using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public record HostInfo
{
    public static readonly Symbol ProductionEnvironment = Environments.Production;
    public static readonly Symbol DevelopmentEnvironment = Environments.Development;

    public Symbol HostKind { get; init; } = Symbol.Empty;
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public ImmutableHashSet<Symbol> RequiredServiceScopes { get; init; } = ImmutableHashSet<Symbol>.Empty;

    public bool IsProductionInstance => Environment == ProductionEnvironment;
    public bool IsDevelopmentInstance => Environment == DevelopmentEnvironment;
}
