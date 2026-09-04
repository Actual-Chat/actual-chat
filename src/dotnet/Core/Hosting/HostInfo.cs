using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Hosting;

/// <summary>
/// Describes the runtime environment and configuration of the current host.
/// </summary>
public sealed record HostInfo
{
    private readonly LazySlim<HostInfo, BaseUrlKind> _baseUrlKindLazy;
    private readonly LazySlim<HostInfo, bool> _isProductionInstanceLazy;
    private readonly LazySlim<HostInfo, bool> _isStagingInstanceLazy;
    private readonly LazySlim<HostInfo, bool> _isDevelopmentInstanceLazy;

    public string BaseUrl { get; init; } = "";
    public HostKind HostKind { get; init; }
    public AppKind AppKind { get; init; }
    public Symbol Environment { get; init; } = Environments.Development;
    public IConfiguration Configuration { get; init; } = null!;
    public string DeviceModel { get; init; } = "Unknown";
    public IReadOnlySet<HostRole> Roles { get; init; } = ImmutableHashSet<HostRole>.Empty;
    public bool IsTested { get; init; }

    // Computed & cached
    public BaseUrlKind BaseUrlKind => _baseUrlKindLazy.Value;
    public bool IsProductionInstance => _isProductionInstanceLazy.Value;
    public bool IsStagingInstance => _isStagingInstanceLazy.Value;
    public bool IsDevelopmentInstance => _isDevelopmentInstanceLazy.Value;

    public HostInfo()
    {
        _baseUrlKindLazy = LazySlim.New(this, static self => {
            var host = self.BaseUrl.EnsureSuffix("/").ToUri().Host;
            return string.Equals(host, CoreConstants.Hosts.Prod, StringComparison.OrdinalIgnoreCase)
                    ? BaseUrlKind.Production
                : string.Equals(host, CoreConstants.Hosts.Dev, StringComparison.OrdinalIgnoreCase)
                    ? BaseUrlKind.Development
                : string.Equals(host, CoreConstants.Hosts.Local, StringComparison.OrdinalIgnoreCase)
                    ? BaseUrlKind.Local
                // Support worktree subdomains: wt1.local.voxt.ai, cdn-wt1.local.voxt.ai, etc.
                : host.EndsWith(CoreConstants.Hosts.LocalSuffix, StringComparison.OrdinalIgnoreCase)
                    ? BaseUrlKind.Local
                : BaseUrlKind.Unknown;
        });
        _isProductionInstanceLazy = LazySlim.New(this,
            static self => self.Environment.Value == Environments.Production);
        _isStagingInstanceLazy = LazySlim.New(this,
            static self => self.Environment.Value == Environments.Staging);
        _isDevelopmentInstanceLazy = LazySlim.New(this,
            static self => self.Environment.Value == Environments.Development);
    }

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
}
