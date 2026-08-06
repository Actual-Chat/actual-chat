using ActualChat.UI.Blazor.Module;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages user account state, authentication flow, and sign-in/sign-out operations in the UI.
/// </summary>
public partial class AccountUI : UIWorkerBase<UIHub>, IComputeService, INotifyInitialized
{
    private static readonly string AuthJsClassName = $"{BlazorUICoreModule.ImportName}.WebAuth";

    private readonly AsyncTaskMethodBuilder _whenReadySource = AsyncTaskMethodBuilderExt.New();
    private readonly MutableState<AccountFull> _ownAccount;
    private readonly MutableState<Moment> _lastChangedAt;
    private readonly MutableState<SignInRequest?> _activeSignInRequest;
    private readonly TimeSpan _maxInvalidationDelay;
    private readonly Lock _postponeOnSignedInWorkflowTasksLock = new();
    private (string Schema, string DisplayName)[]? _cachedAuthSchemas;
    private List<Task>? _postponeOnSignedInWorkflowTasks;
    private string? _pendingRegistrationToken;

    private IAccounts Accounts => Hub.Accounts;

    private IOnboardingUI OnboardingUI => Hub.OnboardingUI;
    private INotificationUI NotificationUI => Hub.NotificationUI;
    private AutoNavigationUI AutoNavigationUI => Hub.AutoNavigationUI;
    private ReloadUI ReloadUI => Hub.ReloadUI;
    private MomentClock CpuClock { get; }
    private LocalStorage LocalStorage => Hub.LocalStorage;

    public Task WhenReady => _whenReadySource.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;
    public IState<Moment> LastChangedAt => _lastChangedAt;
    public IState<SignInRequest?> ActiveSignInRequest => _activeSignInRequest;
    public Moment StartedAt { get; }
    public event Action<AccountFull?>? LoginLogout;

    public AccountUI(UIHub hub) : base(hub)
    {
        CpuClock = Services.Clocks().CpuClock;
        StartedAt = CpuClock.Now;

        _maxInvalidationDelay = TimeSpan.FromSeconds(HostInfo.HostKind.IsServer() ? 0.5 : 2);
        var ownAccountComputed = Computed.GetExisting(() => Accounts.GetOwn(Session, default));
        var ownAccount = ownAccountComputed?.IsConsistent() == true && ownAccountComputed.HasValue
            ? ownAccountComputed.Value
            : null;

        var type = GetType();
        _ownAccount = StateFactory.NewMutable<AccountFull>(new () {
            InitialValue = ownAccount!,
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
        if (ownAccount is not null)
            MarkReady();
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
                        var isSignedIn = !account.IsGuestOrNull();
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
        return OwnAccount.Value is { IsGuest: false };
    }

    // Sign-in / sign-out

    public virtual (string Name, string DisplayName)[] GetAuthSchemas()
        => _cachedAuthSchemas ??= AuthSchema.ToSchemasWithDisplayNames(AuthSchema.AllExternal);

    public async Task SignIn(string schema)
    {
        await SignInBackend(schema).ConfigureAwait(false);
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
        await SignOutBackend().ConfigureAwait(false);
    }

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

    // Protected methods

    protected virtual Task SignInBackend(string schema)
        => JS.InvokeVoidAsync($"{AuthJsClassName}.signIn", schema).AsTask();

    protected virtual Task SignOutBackend()
        => JS.InvokeVoidAsync($"{AuthJsClassName}.signOut").AsTask();

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
                if (hub.AccountUI.OwnAccount.Value.IsGuestOrNull())
                    return;

                if (redirectUrl != null && hub.History.LocalUrl.IsHome()) {
                    // We must await this call to delay the ResetSignInRequest call,
                    // otherwise ProcessOwnAccountChange logic may trigger
                    // the default redirect on sign-in before this one happens.
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
