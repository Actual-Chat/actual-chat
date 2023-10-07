using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public sealed record HostInfo
{
    private readonly BoolOption _isProductionInstance = new();
    private readonly BoolOption _isStagingInstance = new();
    private readonly BoolOption _isDevelopmentInstance = new();
    private string _baseUrl = "";
    private ImmutableHashSet<Symbol>? _requiredServiceScopes;

    public static readonly Symbol ProductionEnvironment = Environments.Production;
    public static readonly Symbol StagingEnvironment = Environments.Staging;
    public static readonly Symbol DevelopmentEnvironment = Environments.Development;

    public AppKind AppKind { get; init; }
    public ClientKind ClientKind { get; init; }
    public bool IsTested { get; init; }
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public string DeviceModel { get; init; } = "Unknown";
    public ImmutableHashSet<Symbol> RequiredServiceScopes => _requiredServiceScopes ??= AppKind.GetRequiredServiceScopes(IsTested);

    public string BaseUrl {
        get {
            if (!_baseUrl.IsNullOrEmpty())
                return _baseUrl;
            if (BaseUrlProvider == null)
                throw StandardError.Internal("Both BaseUrl and BaseUrlProvider are unspecified.");

            _baseUrl = BaseUrlProvider.Invoke();
            if (_baseUrl.IsNullOrEmpty())
                throw StandardError.Internal("BaseUrlProvider returned empty BaseUrl.");

            return _baseUrl;
        }
        init => _baseUrl = value;
    }

    public BaseUrlProvider? BaseUrlProvider { get; init; }

    public bool IsProductionInstance => _isProductionInstance.Value ??= Environment == ProductionEnvironment;
    public bool IsStagingInstance => _isStagingInstance.Value ??= Environment == StagingEnvironment;
    public bool IsDevelopmentInstance => _isDevelopmentInstance.Value ??= Environment == DevelopmentEnvironment;
}
