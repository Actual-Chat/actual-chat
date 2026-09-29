namespace ActualChat.UI.Blazor.Services;

[Flags]
public enum AttentionKind
{
    None = 0,
    Onboarding = 1,
    Bubbles = 2,
    AppReview = 4,
}

public enum AttentionStage
{
    Starting = 0,
    Onboarding,
    Done,
}

/// <summary>
/// Decides when UI the user didn't ask for may take their attention. Once startup settles, it runs one flow
/// per account - a guest signing in restarts it:
/// <list type="number">
/// <item>wait while an <see cref="AttentionHold"/> is active;</item>
/// <item>show onboarding, if the account needs it;</item>
/// <item><see cref="AttentionStage.Done"/>: tips and the review prompt may show whenever no hold is active.</item>
/// </list>
/// Holds mark that the user came for something: a notification tap or an app link (<see cref="AutoNavigationUI"/>),
/// an invite (ChatInvitePage), a sign-in that returns to a link (<see cref="AccountUI.SignInRequest"/>),
/// a share (IncomingShareUI). They outlive a flow restart, and so does <see cref="Suppress"/>,
/// which is for debugUI and tests only. Unrelated to Android's ChatAttentionService.
/// </summary>
public sealed class AttentionUI : UIWorkerBase<UIHub>
{
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultHoldMaxDuration = TimeSpan.FromMinutes(10);

    private readonly MutableState<AttentionStage> _stage;
    private readonly MutableState<AttentionKind> _suppressed;
    private readonly MutableState<ImmutableList<AttentionHold>> _holds;
    private readonly Dictionary<AttentionKind, int> _suppressionCounts = new();
    private CancellationTokenSource? _onboardingCts;

    private IOnboardingUI OnboardingUI => Hub.OnboardingUI;

    public Task WhenSettled { get; }
    public IState<AttentionStage> Stage => _stage;
    public IState<AttentionKind> Suppressed => _suppressed;
    public IState<ImmutableList<AttentionHold>> Holds => _holds;

    public AttentionUI(UIHub hub) : base(hub)
    {
        var type = GetType();
        _stage = StateFactory.NewMutable(AttentionStage.Starting, StateCategories.Get(type, nameof(Stage)));
        _suppressed = StateFactory.NewMutable(AttentionKind.None, StateCategories.Get(type, nameof(Suppressed)));
        _holds = StateFactory.NewMutable(ImmutableList<AttentionHold>.Empty, StateCategories.Get(type, nameof(Holds)));
        Hub.RegisterDisposable(() => {
            foreach (var hold in _holds.Value)
                hold.Stop();
        });
        WhenSettled = Settle();
    }

    // A hold with a leaveTarget lasts as long as the user stays there, so only an explicit one gets a default cap
    public AttentionHold Hold(string reason, LocalUrl? leaveTarget = null, TimeSpan? maxDuration = null)
    {
        var hold = new AttentionHold(this, reason, leaveTarget);
        lock (Lock)
            _holds.Value = _holds.Value.Add(hold);
        Log.LogInformation("+ Hold: {Hold}", hold);
        maxDuration ??= leaveTarget == null ? DefaultHoldMaxDuration : System.Threading.Timeout.InfiniteTimeSpan;
        _ = hold.Run(Hub, maxDuration.GetValueOrDefault(), Log);
        return hold;
    }

    // For tips and the review prompt; onboarding is shown by the flow itself.
    // Tips hide themselves under a modal, but the review prompt must not open over one.
    public async Task<bool> IsAvailableFor(AttentionKind kind, CancellationToken cancellationToken)
    {
        if (await _stage.Use(cancellationToken).ConfigureAwait(false) != AttentionStage.Done)
            return false;
        if ((await _suppressed.Use(cancellationToken).ConfigureAwait(false) & kind) != 0)
            return false;
        if ((await _holds.Use(cancellationToken).ConfigureAwait(false)).Count > 0)
            return false;

        var modals = await ModalUI.ActiveModals.Use(cancellationToken).ConfigureAwait(false);
        return kind == AttentionKind.Bubbles || modals.Count == 0;
    }

    public IDisposable Suppress(AttentionKind kinds)
    {
        // Counted per kind, so a nested suppression doesn't lift the outer one
        CancellationTokenSource? onboardingCts;
        lock (Lock) {
            UpdateSuppressionCounts(kinds, 1);
            onboardingCts = (kinds & AttentionKind.Onboarding) != 0 ? _onboardingCts : null;
        }
        Log.LogInformation("+ Suppress: {Kinds}", kinds);
        onboardingCts.CancelAndDisposeSilently();
        return Disposable.New(() => {
            lock (Lock)
                UpdateSuppressionCounts(kinds, -1);
            Log.LogInformation("- Suppress: {Kinds}", kinds);
        });
    }

    // Protected/internal methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await WhenSettled.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? flowCts = null;
        try {
            UserId? accountId = null;
            var changes = AccountUI.OwnAccount.Computed.Changes(FixedDelayer.NextTick, cancellationToken);
            await foreach (var cAccount in changes.ConfigureAwait(false)) {
                var account = cAccount.Value;
                if (flowCts != null && account.Id == accountId)
                    continue;

                accountId = account.Id;
                flowCts.CancelAndDisposeSilently();
                flowCts = cancellationToken.CreateLinkedTokenSource();
                _ = RunFlow(account, flowCts.Token);
            }
        }
        finally {
            flowCts.CancelAndDisposeSilently();
        }
    }

    internal bool RemoveHold(AttentionHold hold)
    {
        lock (Lock) {
            var holds = _holds.Value;
            var newHolds = holds.Remove(hold);
            if (ReferenceEquals(holds, newHolds))
                return false;

            _holds.Value = newHolds;
        }
        Log.LogInformation("- Hold: {Reason}", hold.Reason);
        return true;
    }

    // Private methods

    private async Task Settle()
    {
        await Hub.LoadingUI.WhenRendered.ConfigureAwait(false);
        await AccountUI.WhenReady.ConfigureAwait(false);
        // The narrow-screen start opens /chats first and then the chat it was meant to show
        await Dispatcher.InvokeAsync(() => History.WhenNavigationCompletedOrTimeout()).ConfigureAwait(false);
        await Clocks.CpuClock.Delay(SettleDelay).ConfigureAwait(false);
    }

    private void UpdateSuppressionCounts(AttentionKind kinds, int delta)
    {
        var suppressed = AttentionKind.None;
        foreach (var kind in Enum.GetValues<AttentionKind>()) {
            if (kind == AttentionKind.None)
                continue;

            var count = _suppressionCounts.GetValueOrDefault(kind);
            if ((kinds & kind) != 0)
                _suppressionCounts[kind] = count += delta;
            if (count > 0)
                suppressed |= kind;
        }
        _suppressed.Value = suppressed;
    }

    private async Task RunFlow(AccountFull account, CancellationToken cancellationToken)
    {
        Log.LogInformation("Flow for {AccountId}: started", account.Id);
        _stage.Value = AttentionStage.Onboarding;
        try {
            if (!account.IsGuest)
                await RunOnboarding(cancellationToken).ConfigureAwait(false);
            _stage.Value = AttentionStage.Done;
            Log.LogInformation("Flow for {AccountId}: done", account.Id);
        }
        catch (Exception e) when (e.IsCancellationOf(cancellationToken)) {
            // A newer account took over
        }
        catch (Exception e) {
            Log.LogError(e, "Flow for {AccountId} failed", account.Id);
            _stage.Value = AttentionStage.Done;
        }
    }

    private async Task RunOnboarding(CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        lock (Lock) {
            if ((_suppressed.Value & AttentionKind.Onboarding) != 0)
                return;

            _onboardingCts = cts;
        }
        ModalRef? modalRef = null;
        try {
            // Decided once no hold is active, so the answer reflects the settings by then
            await WhenFree(cancellationToken: cts.Token).ConfigureAwait(false);
            if (!await OnboardingUI.ShouldBeShown(cts.Token).ConfigureAwait(false))
                return;

            await WhenFree(mustWaitForModals: true, cts.Token).ConfigureAwait(false);
            modalRef = await Dispatcher.InvokeAsync(OnboardingUI.Show).ConfigureAwait(false);
            await modalRef.WhenClosed.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // Suppressed
            if (modalRef != null)
                await Dispatcher.InvokeAsync(() => modalRef.Close(true)).ConfigureAwait(false);
        }
        finally {
            lock (Lock)
                if (_onboardingCts == cts)
                    _onboardingCts = null;
        }
    }

    private async Task WhenFree(bool mustWaitForModals = false, CancellationToken cancellationToken = default)
    {
        var cIsFree = await Computed
            .New(Services, async ct => {
                if ((await _holds.Use(ct).ConfigureAwait(false)).Count > 0)
                    return false;

                return !mustWaitForModals || (await ModalUI.ActiveModals.Use(ct).ConfigureAwait(false)).Count == 0;
            })
            .Update(cancellationToken)
            .ConfigureAwait(false);
        await cIsFree.When(x => x, cancellationToken).ConfigureAwait(false);
    }
}
