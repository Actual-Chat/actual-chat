using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatAudioUI
{
    // Absent on platforms without projection, which is why it isn't a required dependency.
    private ICarConnection? CarConnection => field ??= Hub.Services.GetService<ICarConnection>();

    [ComputeMethod]
    public virtual async Task<CarAudioRoute> GetCarAudioRoute(CancellationToken cancellationToken)
    {
        var carConnection = CarConnection;
        if (carConnection == null)
            return CarAudioRoute.Default;

        var isProjectionActive = await carConnection
            .IsProjectionActive(cancellationToken)
            .ConfigureAwait(false);
        if (!isProjectionActive)
            return CarAudioRoute.Default;

        var settings = await UserSettingsUI.UserCarAudioSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return CarAudioRoute.For(true, settings);
    }
}
