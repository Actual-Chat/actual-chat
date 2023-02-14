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
    private BrowserInfo BrowserInfo { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;
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
        BrowserInfo = services.GetRequiredService<BrowserInfo>();
        Clocks = services.Clocks();

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

    public void SignOut(LocalUrl redirectUrl = default)
    {
        var isMauiApp = Services.GetRequiredService<HostInfo>().AppKind.IsMauiApp();
        if (isMauiApp)
            _ = SignOutInMaui(redirectUrl, default);
        else
            _ = HardRedirect(redirectUrl);
    }

    public void ReloadOnSignOut()
    {
        var isMauiApp = Services.GetRequiredService<HostInfo>().AppKind.IsMauiApp();
        if (isMauiApp)
            RedirectOnSignOut();
        else
            _ = HardRedirect();
    }

    private async Task SignOutInMaui(LocalUrl redirectUrl, CancellationToken cancellationToken)
    {
        var account = await OwnAccount.Use(cancellationToken).ConfigureAwait(true);
        var isGuest = account.IsGuest;
        await Services.GetRequiredService<IClientAuth>().SignOut().ConfigureAwait(true);
        if (isGuest)
            RedirectOnSignOut(redirectUrl);
        else {
            // Do nothing here now.
            // SignOutReloader should initiate redirect after OwnAccount is invalidated.
        }
    }

    private ValueTask HardRedirect(LocalUrl redirectUrl = default)
        => BrowserInfo.HardRedirect(Links.SignOut(redirectUrl));

    private void RedirectOnSignOut(LocalUrl redirectUrl = default)
    {
        if (redirectUrl.IsHome())
            Services.GetRequiredService<AutoNavigationUI>().MustNavigateToChatsOnSignIn = true;
        Services.GetRequiredService<NavigationManager>().NavigateTo(redirectUrl);
    }
}
