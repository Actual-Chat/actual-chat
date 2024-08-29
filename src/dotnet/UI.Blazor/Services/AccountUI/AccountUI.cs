using ActualChat.Hosting;
using ActualChat.Users;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI : ScopedWorkerBase<UIHub>, IComputeService, INotifyInitialized
{
    private readonly TaskCompletionSource _whenLoadedSource = TaskCompletionSourceExt.New();
    private readonly MutableState<AccountFull> _ownAccount;
    private readonly MutableState<Moment> _lastChangedAt;
    private readonly TimeSpan _maxInvalidationDelay;
    private IClientAuth? _clientAuth;
    private INotificationUI? _notificationUI;
    private SignInRequesterUI? _signInRequesterUI;

    private IAccounts Accounts => Hub.Accounts;
    private AppBlazorCircuitContext CircuitContext => Hub.CircuitContext;
    private SignInRequesterUI SignInRequesterUI => _signInRequesterUI ??= Services.GetRequiredService<SignInRequesterUI>();
    private IClientAuth ClientAuth => _clientAuth ??= Services.GetRequiredService<IClientAuth>();
    private History History => Hub.History;
    private INotificationUI NotificationUI => _notificationUI ??= Services.GetRequiredService<INotificationUI>();
    private MomentClock CpuClock { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;
    public IState<Moment> LastChangedAt => _lastChangedAt;
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
}
