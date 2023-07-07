using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public sealed record HostInfo
{
    private readonly BoolOption _isProductionInstance = new();
    private readonly BoolOption _isStagingInstance = new();
    private readonly BoolOption _isDevelopmentInstance = new();
    private readonly string _baseUrl = "";
    private ImmutableHashSet<Symbol>? _requiredServiceScopes;

    public static Symbol ProductionEnvironment { get; } = Environments.Production;
    public static Symbol StagingEnvironment { get; } = Environments.Staging;
    public static Symbol DevelopmentEnvironment { get; } = Environments.Development;

    public AppKind AppKind { get; init; }
    public ClientKind ClientKind { get; init; }
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public string DeviceModel { get; init; } = "Unknown";
    public ImmutableHashSet<Symbol> RequiredServiceScopes => _requiredServiceScopes ??= AppKind.GetRequiredServiceScopes();

    public string BaseUrl {
        get {
            if (!_baseUrl.IsNullOrEmpty())
                return _baseUrl;
            if (BaseUrlProvider == null)
                throw StandardError.Constraint<InvalidOperationException>("BaseUrlProvider is not specified");

            return BaseUrlProvider();
        }
        init => _baseUrl = value;
    }

    public Func<string>? BaseUrlProvider { get; init; }

    public bool IsProductionInstance => _isProductionInstance.Value ??= Environment == ProductionEnvironment;
    public bool IsStagingInstance => _isStagingInstance.Value ??= Environment == StagingEnvironment;
    public bool IsDevelopmentInstance => _isDevelopmentInstance.Value ??= Environment == DevelopmentEnvironment;
    public Platform Platform { get; set; }
}
