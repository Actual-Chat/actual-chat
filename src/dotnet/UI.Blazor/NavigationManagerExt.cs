namespace ActualChat.UI.Blazor;

public static class NavigationManagerExt
{
    public static LocalUrl GetLocalUrl(this NavigationManager nav)
        => new(nav.ToBaseRelativePath(nav.Uri));
}
