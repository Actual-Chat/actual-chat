using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

public sealed record HostInfo
{
    private LazySlim<BaseUrlKind>? _baseUrlKind;
    private LazySlim<bool>? _isProductionInstance;
    private LazySlim<bool>? _isDevelopmentInstance;

    public string BaseUrl { get; init; } = "";
    public HostKind HostKind { get; init; }
    public AppKind AppKind { get; init; }
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public string DeviceModel { get; init; } = "Unknown";
    public IReadOnlySet<HostRole> Roles { get; init; } = ImmutableHashSet<HostRole>.Empty;
    public bool IsTested { get; init; }

    // Computed & cached
    public BaseUrlKind BaseUrlKind => (_baseUrlKind ??= LazySlim.New(GetBaseUrlKind(BaseUrl))).Value;
    public bool IsProductionInstance => (_isProductionInstance ??= LazySlim.New(IsProductionEnv())).Value;
    public bool IsDevelopmentInstance => (_isDevelopmentInstance ??= LazySlim.New(IsDevelopmentEnv())).Value;

    public bool HasRole(HostRole role) => Roles.Contains(role);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(BaseUrl)).Append(" = '").Append(BaseUrl).Append("', ");
        builder.Append(nameof(HostKind)).Append(" = ").Append(HostKind).Append(", ");
        builder.Append(nameof(AppKind)).Append(" = ").Append(AppKind).Append(", ");
        builder.Append(nameof(Roles)).Append(" = [").Append(Roles.ToDelimitedString()).Append("], ");
        builder.Append(nameof(Environment)).Append(" = '").Append(Environment).Append("', ");
        builder.Append(nameof(DeviceModel)).Append(" = '").Append(DeviceModel).Append("', ");
        builder.Append(nameof(IsTested)).Append(" = ").Append(IsTested);
        return true;
    }

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
