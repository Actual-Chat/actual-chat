using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class AuthUI : WorkerBase
{
    private readonly IStoredState<bool> _hasEverSignedIn;
    private IAuth Auth { get; }
    private Session Session { get; }

    public IState<bool> HasEverSignedIn => _hasEverSignedIn;

    public AuthUI(IServiceProvider services)
    {
        Auth = services.GetRequiredService<IAuth>();
        Session = services.GetRequiredService<Session>();
        _hasEverSignedIn = services.StateFactory().NewKvasStored<bool>(new (services.LocalSettings(), nameof(HasEverSignedIn)));
        Start();
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        await _hasEverSignedIn.WhenRead.ConfigureAwait(false);
        if (_hasEverSignedIn.Value)
            return;

        var cAuthInfo = await Computed.Capture(() => Auth.GetAuthInfo(Session, cancellationToken)).ConfigureAwait(false);
        await cAuthInfo.When(x => x?.IsAuthenticated() == true, cancellationToken).ConfigureAwait(false);
        _hasEverSignedIn.Value = true;
    }
}
