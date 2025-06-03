using ActualChat.Kvas;
using ActualChat.Users;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.Services;

public partial class UpgradeUI : UIWorkerBase<UIHub>, IComputeService, INotifyInitialized
{
    private readonly ComputedState<bool> _upgradeRequiredState;
    private readonly IStoredState<LocalClientCompatibility?> _storedState;

    private string ClientVersion => AppInfo.Version;
    private ISystemProperties SystemProperties { get; }

    public IState<bool> UpgradeRequiredState => _upgradeRequiredState;

    public UpgradeUI(UIHub hub) : base(hub)
    {
        SystemProperties = hub.Services.GetRequiredService<ISystemProperties>();
        var stateFactory = hub.Services.StateFactory();
        _storedState = StateFactory.NewKvasStored<LocalClientCompatibility?>(new(LocalSettings, LocalClientCompatibility.KvasKey));
        _upgradeRequiredState = stateFactory.NewComputed(ComputeUpgradeRequired);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    private async Task<bool> ComputeUpgradeRequired(CancellationToken cancellationToken)
    {
        var clientVersion = ClientVersion;
        if (clientVersion.IsNullOrEmpty())
            return false;

        await _storedState.WhenRead.ConfigureAwait(false);
        var storedState = await _storedState.Use(cancellationToken).ConfigureAwait(false);
        if (storedState is null || !OrdinalEquals(storedState.ClientVersion, clientVersion))
            return false;

        return storedState.ClientCompatibility is SystemProperties_ClientCompatibility.Incompatible;
    }
}
