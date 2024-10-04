using ActualChat.Hosting;
using ActualChat.Users;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI : ScopedWorkerBase<UIHub>, IComputeService, INotifyInitialized
{
    private readonly TaskCompletionSource _whenLoadedSource = TaskCompletionSourceExt.New();
    private readonly MutableState<AccountFull> _ownAccount;
    private readonly MutableState<Moment> _lastChangedAt;
    private readonly MutableState<SignInRequest?> _activeSignInRequest;
    private readonly TimeSpan _maxInvalidationDelay;
    private readonly object _postponeOnSignedInWorkflowTasksLock = new();
    private IClientAuth? _clientAuth;
    private List<Task>? _postponeOnSignedInWorkflowTasks;

    private IAccounts Accounts => Hub.Accounts;
    private AppBlazorCircuitContext CircuitContext => Hub.CircuitContext;
    private IClientAuth ClientAuth => _clientAuth ??= Services.GetRequiredService<IClientAuth>();
    private IOnboardingUI OnboardingUI => Hub.OnboardingUI;
    private INotificationUI NotificationUI => Hub.NotificationUI;
    private AutoNavigationUI AutoNavigationUI => Hub.AutoNavigationUI;
    private ReloadUI ReloadUI => Hub.ReloadUI;
    private History History => Hub.History;
    private Dispatcher Dispatcher => Hub.Dispatcher;
    private MomentClock CpuClock { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;
    public IState<Moment> LastChangedAt => _lastChangedAt;
    public IState<SignInRequest?> ActiveSignInRequest => _activeSignInRequest;
    public Moment StartedAt { get; }
    public event Action<AccountFull>? Changed;

    public AccountUI(UIHub hub) : base(hub)
    {
        CpuClock = Services.Clocks().CpuClock;
        StartedAt = CpuClock.Now;

        _maxInvalidationDelay = TimeSpan.FromSeconds(HostInfo.HostKind.IsServer() ? 0.5 : 2);
        var ownAccountComputed = Computed.GetExisting(() => Accounts.GetOwn(Session, default));
        var ownAccount = ownAccountComputed?.IsConsistent() == true &&  ownAccountComputed.HasValue ? ownAccountComputed.Value : null;
        var initialOwnAccount = ownAccount ?? AccountFull.Loading;

        var type = GetType();
        _ownAccount = StateFactory.NewMutable<AccountFull>(new () {
            InitialValue = initialOwnAccount,
            Category = StateCategories.Get(type, nameof(OwnAccount)),
        });
        _lastChangedAt = StateFactory.NewMutable<Moment>(new () {
            InitialValue = StartedAt,
            Category = StateCategories.Get(type, nameof(OwnAccount)),
        });
        _activeSignInRequest = StateFactory.NewMutable<SignInRequest?>(new () {
            InitialValue = null,
            Category = StateCategories.Get(type, nameof(ActiveSignInRequest)),
        });
        if (!ReferenceEquals(initialOwnAccount, AccountFull.Loading))
            _whenLoadedSource.TrySetResult();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public TimeSpan GetPostChangeInvalidationDelay()
        => GetPostChangeInvalidationDelay(TimeSpan.FromSeconds(2));
    public TimeSpan GetPostChangeInvalidationDelay(TimeSpan maxInvalidationDelay)
    {
        maxInvalidationDelay = maxInvalidationDelay.Clamp(default, _maxInvalidationDelay);
        var changedAt = Moment.Max(LastChangedAt.Value, StartedAt + TimeSpan.FromSeconds(1));
        return (changedAt + maxInvalidationDelay - CpuClock.Now).Positive();
    }

    public async Task<bool> RequestSignInFromHomePage(string reason, string? redirectUrl)
    {
        var mySignInRequest = new SignInRequest(Hub, reason, redirectUrl);
        _activeSignInRequest.Value = mySignInRequest;
        try {
            await History.NavigateTo(Links.Home, true).ConfigureAwait(false);
            var c = await Computed.New(Services,
                    async ct => {
                        var account = await OwnAccount.Use(ct).ConfigureAwait(false);
                        var historyItem = await History.State.Use(ct).ConfigureAwait(false);
                        var url = new LocalUrl(historyItem.Url);
                        var signInRequest = await _activeSignInRequest.Use(ct).ConfigureAwait(false);
                        var isSignedIn = !account.IsGuestOrNone;
                        var mustComplete = isSignedIn || signInRequest != mySignInRequest || !url.IsHome();
                        return mustComplete;
                    })
                .Update()
                .ConfigureAwait(false);
            await c.When(x => x).ConfigureAwait(false);
        }
        catch {
            // Intended
        }
        TryResetSignInRequest(mySignInRequest);
        return !OwnAccount.Value.IsGuestOrNone;
    }

    // IClientAuth wrapping methods

#pragma warning disable CS0618 // Type or member is obsolete

    public (string Name, string DisplayName)[] GetAuthSchemas()
        => ClientAuth.GetSchemas();

    public async Task SignIn(string schema)
    {
        await ClientAuth.SignIn(schema).ConfigureAwait(false);
        // TODO(AY): Make it reliable
        await NotificationUI.EnsureDeviceRegistered(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task SignOut()
    {
        try {
            // TODO(AY): Make it reliable
            await NotificationUI.DeregisterDevice(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "SignOut: failed to deregister device");
        }
        await ClientAuth.SignOut().ConfigureAwait(false);
    }

#pragma warning restore CS0618 // Type or member is obsolete

    public Task SignOutEverywhere(bool force = true)
        => Commander.Call(new Auth_SignOut(Session, force) { KickAllUserSessions = true });

    public Task Kick(Session session, string otherSessionHash, bool force = false)
        => Commander.Call(new Auth_SignOut(session, otherSessionHash, force));

    public void PostponeOnSignInWorkflow(Task taskToAwait)
    {
        DebugLog?.LogInformation("Adding postpone on sign-in workflow task");
        lock (_postponeOnSignedInWorkflowTasksLock) {
            if (taskToAwait.IsCompleted)
                return;

            _postponeOnSignedInWorkflowTasks ??= new List<Task>();
            _postponeOnSignedInWorkflowTasks.Add(taskToAwait);
        }
    }

    // Private methods

    private void TryResetSignInRequest(SignInRequest expected)
        => _activeSignInRequest.Set(expected, (expected1, x) => ReferenceEquals(x.Value, expected1) ? null : x.Value);

    private async Task PostponeOnSignedInWorkflow()
    {
        // TODO(DF): rewrite with using AsyncCountdownEvent.
        // TODO(DF): May be we can combine this with SignInRequest.
        while (true) {
            Task[]? tasksToAwait = null;
            lock (_postponeOnSignedInWorkflowTasksLock) {
                if (_postponeOnSignedInWorkflowTasks is not null) {
                    _postponeOnSignedInWorkflowTasks.RemoveAll(t => t.IsCompleted);
                    if (_postponeOnSignedInWorkflowTasks.Count > 0)
                        tasksToAwait = _postponeOnSignedInWorkflowTasks.ToArray();
                }
            }

            if (tasksToAwait is null || tasksToAwait.Length == 0)
                return;

            try {
                DebugLog?.LogInformation("{NumberOfTasksToAwait} tasks are postponing OnSignedInWorkflow", tasksToAwait.Length);
                await Task.WhenAny(tasksToAwait).ConfigureAwait(false);
            }
            catch {
                // Ignore intended
            }

            lock (_postponeOnSignedInWorkflowTasksLock) {
                if (_postponeOnSignedInWorkflowTasks is not null) {
                    foreach (var task in tasksToAwait)
                        if (task.IsCompleted)
                            _postponeOnSignedInWorkflowTasks!.Remove(task);
                    if (_postponeOnSignedInWorkflowTasks!.Count == 0)
                        _postponeOnSignedInWorkflowTasks = null;
                }
            }
        }
    }

    // Nested types

    public sealed class SignInRequest(UIHub hub, string reason, string? redirectUrl)
    {
        public bool IsShown { get; private set; }
        public bool IsCompleted { get; private set; }

        public async Task Show()
        {
            if (IsShown)
                return;

            IsShown = true;
            try {
                var modalRef = await hub.ModalUI.Show(new SignInModal.Model(reason)).ConfigureAwait(true);
                await modalRef.WhenClosed.ConfigureAwait(true);
                if (hub.AccountUI.OwnAccount.Value.IsGuestOrNone)
                    return;

                if (redirectUrl != null && hub.History.LocalUrl.IsHome()) {
                    // We must await this call to delay ResetSignInRequest call,
                    // otherwise ProcessOwnAccountChange logic may trigger
                    // default redirect on sign-in before this one happens.
                    await hub.History.NavigateTo(redirectUrl, true).ConfigureAwait(true);
                }
            }
            finally {
                IsCompleted = true;
                hub.AccountUI.TryResetSignInRequest(this);
            }
        }
    }
}
