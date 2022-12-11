namespace ActualChat.Users.UI.Blazor.Services;

public partial class AccountUI : WorkerBase
{
    private readonly TaskSource<Unit> _whenLoaded;
    private readonly IMutableState<AccountFull> _ownAccount;

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;
    private ILogger Log { get; }

    public Task WhenLoaded => _whenLoaded.Task;
    public IState<AccountFull> OwnAccount => _ownAccount;

    public AccountUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Services = services;
        StateFactory = services.StateFactory();
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        Clocks = services.Clocks();

        _whenLoaded = TaskSource.New<Unit>(true);
        var ownAccountTask = Accounts.GetOwn(Session, default);
 #pragma warning disable VSTHRD002
        var ownAccount = ownAccountTask.IsCompletedSuccessfully
            ? ownAccountTask.Result
            : AccountFull.Loading;
 #pragma warning restore VSTHRD002
        _ownAccount = StateFactory.NewMutable<AccountFull>(new() { InitialValue = ownAccount });
        Start();
    }
}
