
namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public sealed class Features_EnableAnonymousChat : FeatureDef<bool>, IClientFeatureDef
{
    public static bool IsEnabled { get; } = !(OSInfo.IsIOS || OSInfo.IsMacOS || OSInfo.IsAndroid || OSInfo.IsWindows);

    public override Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var hostInfo = services.HostInfo();
        return Task.FromResult(!hostInfo.AppKind.IsMaui());
    }
}
