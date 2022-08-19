using Microsoft.AspNetCore.Components;
namespace ActualChat.App.Maui.Services;

public class NavigationInterceptor
{
    private NavigationManager? _nav;
    private readonly Uri _baseUri;

    public NavigationInterceptor(ClientAppSettings appSettings)
        => _baseUri = new Uri(appSettings.BaseUri);

    internal void Initialize(NavigationManager nav)
        => _nav = nav;

    internal bool TryIntercept(Uri uri)
    {
        if (_nav == null)
            return false;

        if (_baseUri.IsBaseOf(uri)) {
            var relativeUri = _baseUri.MakeRelativeUri(uri);
            _nav.NavigateTo(relativeUri.ToString());
            return true;
        }
        return false;
    }
}
