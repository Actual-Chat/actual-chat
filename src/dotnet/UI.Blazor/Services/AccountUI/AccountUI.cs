using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI : WorkerBase
{
    private readonly TaskSource<Unit> _whenLoadedSource;
    private readonly IMutableState<AccountFull> _ownAccount;

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private History History { get; }
    private ILogger Log { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;

    public AccountUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        StateFactory = services.StateFactory();
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        History = services.GetRequiredService<History>();

        _whenLoadedSource = TaskSource.New<Unit>(true);
        var ownAccountTask = Accounts.GetOwn(Session, default);
 #pragma warning disable VSTHRD002
        var ownAccount = ownAccountTask.IsCompletedSuccessfully
            ? ownAccountTask.Result
            : AccountFull.Loading;
 #pragma warning restore VSTHRD002
        _ownAccount = StateFactory.NewMutable<AccountFull>(new () {
            InitialValue = ownAccount,
            Category = StateCategories.Get(GetType(), nameof(OwnAccount)),
        });
        Start();
    }

    public async Task SignOut()
    {
        if (OwnAccount.Value.IsGuest)
            return;

        if (History.HostInfo.AppKind.IsMauiApp()) {
            // MAUI scenario:
            // - Sign-out natively
            // - Do nothing, coz SignOutReloader will do the rest
            await Services.GetRequiredService<IClientAuth>().SignOut();
            return;
        }

        // Blazor Server/WASM scenario:
        // - Redirect to sign-out page, which redirects to home page after sign-out completion
        // - SignOutReloader doesn't get a chance to reload anything in this case - which is fine.
        await History.HardNavigateTo(Links.SignOut());
    }
}
