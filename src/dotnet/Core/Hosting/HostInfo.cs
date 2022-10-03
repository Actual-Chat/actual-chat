using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public sealed record HostInfo
{
    private bool? _isProductionInstance;
    private bool? _isStagingInstance;
    private bool? _isDevelopmentInstance;
    private readonly string _baseUrl = "";

    public static Symbol ProductionEnvironment { get; } = Environments.Production;
    public static Symbol StagingEnvironment { get; } = Environments.Staging;
    public static Symbol DevelopmentEnvironment { get; } = Environments.Development;

    public Symbol HostKind { get; init; } = Symbol.Empty;
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public ImmutableHashSet<Symbol> RequiredServiceScopes { get; init; } = ImmutableHashSet<Symbol>.Empty;

    public string BaseUrl {
        get {
            if (!string.IsNullOrEmpty(_baseUrl))
                return _baseUrl;
            if (BaseUrlProvider == null)
                throw StandardError.Constraint<InvalidOperationException>("BaseUrlProvider is not specified");
            return BaseUrlProvider();
        }
        init => _baseUrl = value;
    }

    public Func<string>? BaseUrlProvider { get; init; }

    public bool IsProductionInstance => _isProductionInstance ??= Environment == ProductionEnvironment;
    public bool IsStagingInstance => _isStagingInstance ??= Environment == StagingEnvironment;
    public bool IsDevelopmentInstance => _isDevelopmentInstance ??= Environment == DevelopmentEnvironment;
}
