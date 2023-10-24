namespace ActualChat.UI.Blazor.Services;

public class SignInRequesterUI(IServiceProvider services)
{
    private History? _history;
    private LocalUrl? _localUrl;

    public SignInRequest? Request { get; private set; }

    private History History => _history ??= services.GetRequiredService<History>();

    public void Clear()
        => Request = null;

    public Task NavigateToSignIn(string reason, string redirectTo)
    {
        Request = new SignInRequest(reason, redirectTo);
        return History.NavigateTo(Links.Home, true);
    }

    public void ClearRequestOnLocationChange()
    {
        History.LocationChanged -= OnLocationChanged;
        _localUrl = History.LocalUrl;
        History.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (History.LocalUrl.Equals(_localUrl))
            return;

        Clear();
        History.LocationChanged -= OnLocationChanged;
    }

    public record SignInRequest(string Reason, string RedirectTo);
}
