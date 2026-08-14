using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

#pragma warning disable MA0084

public partial class ActivitiesUI : UIWorkerBase<AppUIHub>, IAppActivityState, IComputeService
{
    private readonly MutableState<AppActivityState> _state;
    private IActivitySource[]? _sources;

    protected BackgroundStateTracker BackgroundStateTracker
        => field ??= Services.GetRequiredService<BackgroundStateTracker>();

    private IActivitySource[] Sources
        // Resolved lazily rather than in the constructor: sources are only needed once
        // GetActivitySet first runs, and the set of registered sources never changes at runtime.
        => _sources ??= Services.GetServices<IActivitySource>().ToArray();

    public IState<AppActivityState> State => _state;
    public IState<bool> IsBackground => BackgroundStateTracker.IsBackground;

    public ActivitiesUI(AppUIHub hub) : base(hub)
        => _state = StateFactory.NewMutable(
            AppActivityState.Foreground,
            StateCategories.Get(typeof(ActivitiesUI), nameof(State)));

    [ComputeMethod]
    public virtual async Task<ActivitySet> GetActivitySet(CancellationToken cancellationToken)
    {
        List<ActivityInfo>? activities = null;
        foreach (var source in Sources) {
            if (await source.GetActivity(cancellationToken).ConfigureAwait(false) is { } activity)
                (activities ??= []).Add(activity);
        }
        return activities is null ? ActivitySet.Empty : new ActivitySet(activities);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken)
        // Overridable hook used by tests; production has no signal beyond the states below.
        => Task.FromResult(false);

    [ComputeMethod]
    protected virtual async Task<bool> HasAudioActivity(CancellationToken cancellationToken)
    {
        // Listening/recording intent, not playback: a chat marked listening whose player hasn't
        // spun up yet must already count as active, or backgrounding would suspend the app
        // before the player starts. The set's audio activity requires actual playback.
        var replayState = await Hub.ChatAudioUI.ReplayState.Use(cancellationToken).ConfigureAwait(false);
        if (replayState is not null)
            return true;

        var activeChats = await Hub.ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        return activeChats.Any(c => c.IsListening || c.IsRecording);
    }
}
