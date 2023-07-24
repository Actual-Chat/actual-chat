using ActualChat.Users;
using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI : WorkerBase, IComputeService, INotifyInitialized, IHasServices
{
    private readonly TaskCompletionSource _whenLoadedSource = TaskCompletionSourceExt.New();
    private readonly IMutableState<AccountFull> _ownAccount;
    private AppBlazorCircuitContext? _blazorCircuitContext;
    private IClientAuth? _clientAuth;
    private ILogger? _log;

    private AppBlazorCircuitContext BlazorCircuitContext =>
        _blazorCircuitContext ??= Services.GetRequiredService<AppBlazorCircuitContext>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public IAccounts Accounts { get; }
    public IClientAuth ClientAuth => _clientAuth ??= Services.GetRequiredService<IClientAuth>();

    public Task WhenLoaded => _whenLoadedSource.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;
    public event Action<AccountFull>? OwnAccountChanged;

    public AccountUI(IServiceProvider services)
    {
        Services = services;
        Session = services.Session();
        Accounts = services.GetRequiredService<IAccounts>();

        var ownAccountTask = Accounts.GetOwn(Session, default);
 #pragma warning disable VSTHRD002, VSTHRD104
        AccountFull initialOwnAccount = ownAccountTask.IsCompletedSuccessfully
            ? ownAccountTask.Result
            : AccountFull.Loading;
 #pragma warning restore VSTHRD002, VSTHRD104

        var stateFactory = services.StateFactory();
        _ownAccount = stateFactory.NewMutable<AccountFull>(new () {
            InitialValue = initialOwnAccount,
            Category = StateCategories.Get(GetType(), nameof(OwnAccount)),
        });
        if (!ReferenceEquals(initialOwnAccount, AccountFull.Loading))
            _whenLoadedSource.TrySetResult();
    }

    void INotifyInitialized.Initialized()
        => this.Start();
}
