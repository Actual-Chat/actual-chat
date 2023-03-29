using ActualChat.Hosting;
using ActualChat.Users;
using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI : WorkerBase, IComputeService, INotifyInitialized
{
    private readonly TaskSource<Unit> _whenLoadedSource;
    private readonly IMutableState<AccountFull> _ownAccount;

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private ILogger Log { get; }
    private Tracer Tracer { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;

    public AccountUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Tracer = services.Tracer(GetType());

        StateFactory = services.StateFactory();
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();

        _whenLoadedSource = TaskSource.New<Unit>(true);
        var ownAccountTask = Accounts.GetOwn(Session, default);
 #pragma warning disable VSTHRD002
        AccountFull ownAccount;
        if (ownAccountTask.IsCompletedSuccessfully) {
            ownAccount = ownAccountTask.Result;
            Tracer.Point(".ctor: OwnAccount is already loaded");
        }
        else {
            ownAccount = AccountFull.Loading;
            Tracer.Point(".ctor: OwnAccount is not loaded yet");
        }
 #pragma warning restore VSTHRD002
        _ownAccount = StateFactory.NewMutable<AccountFull>(new () {
            InitialValue = ownAccount,
            Category = StateCategories.Get(GetType(), nameof(OwnAccount)),
        });
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
        await history.HardNavigateTo(Links.SignOut());
    }
}
