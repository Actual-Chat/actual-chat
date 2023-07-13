using ActualChat.Hosting;
using ActualChat.Users;
using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI : WorkerBase, IComputeService, INotifyInitialized
{
    private readonly TaskCompletionSource _whenLoadedSource = TaskCompletionSourceExt.New();
    private readonly IMutableState<AccountFull> _ownAccount;

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private ILogger Log { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;

    public AccountUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        StateFactory = services.StateFactory();
        Session = services.Session();
        Accounts = services.GetRequiredService<IAccounts>();

        var ownAccountTask = Accounts.GetOwn(Session, default);
 #pragma warning disable VSTHRD002, VSTHRD104
        AccountFull initialOwnAccount = ownAccountTask.IsCompletedSuccessfully
            ? ownAccountTask.Result
            : AccountFull.Loading;
 #pragma warning restore VSTHRD002, VSTHRD104
        _ownAccount = StateFactory.NewMutable<AccountFull>(new () {
            InitialValue = initialOwnAccount,
            Category = StateCategories.Get(GetType(), nameof(OwnAccount)),
        });
        if (!ReferenceEquals(initialOwnAccount, AccountFull.Loading))
            _whenLoadedSource.TrySetResult();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public async Task SignOut()
    {
        if (OwnAccount.Value.IsGuest)
            return;

        var history = Services.GetRequiredService<History>();
        if (history.HostInfo.AppKind.IsMauiApp()) {
            // MAUI scenario:
            // - Sign-out natively
            // - Do nothing, coz SignOutReloader will do the rest
            await Services.GetRequiredService<IClientAuth>().SignOut();
            return;
        }

        // Blazor Server/WASM scenario:
        // - Redirect to sign-out page, which redirects to home page after sign-out completion
        // - SignOutReloader doesn't get a chance to reload anything in this case - which is fine.
        history.ForceReload(Links.SignOut());
    }
}
