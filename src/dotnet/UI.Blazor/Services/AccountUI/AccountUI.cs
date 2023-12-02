using ActualChat.Hosting;
using ActualChat.Users;
using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI : ScopedWorkerBase, IComputeService, INotifyInitialized
{
    private readonly TaskCompletionSource _whenLoadedSource = TaskCompletionSourceExt.New();
    private readonly IMutableState<AccountFull> _ownAccount;
    private readonly IMutableState<Moment> _lastChangedAt;
    private readonly TimeSpan _maxInvalidationDelay;
    private AppBlazorCircuitContext? _blazorCircuitContext;
    private IClientAuth? _clientAuth;
    private SignInRequesterUI? _signInRequesterUI;

    private AppBlazorCircuitContext BlazorCircuitContext =>
        _blazorCircuitContext ??= Services.GetRequiredService<AppBlazorCircuitContext>();
    private SignInRequesterUI SignInRequesterUI =>
        _signInRequesterUI ??= Services.GetRequiredService<SignInRequesterUI>();
    private IClientAuth ClientAuth => _clientAuth ??= Services.GetRequiredService<IClientAuth>();

    public IAccounts Accounts { get; }
    public IMomentClock Clock { get; }

    public new Session Session => Scope.Session;
    public Task WhenLoaded => _whenLoadedSource.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;
    public IState<Moment> LastChangedAt => _lastChangedAt;
    public Moment StartedAt { get; }
    public event Action<AccountFull>? Changed;

    public AccountUI(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Clock = services.Clocks().CpuClock;

        StartedAt = Clock.Now;
        _maxInvalidationDelay = TimeSpan.FromSeconds(HostInfo.AppKind.IsServer() ? 0.5 : 2);
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
        return (changedAt + maxInvalidationDelay - Clock.Now).Positive();
    }

    public Task SignOut()
        => ClientAuth.SignOut();
}
