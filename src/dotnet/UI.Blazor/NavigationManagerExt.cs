namespace ActualChat.UI.Blazor;

public static class NavigationManagerExt
{
    public static string GetRelativePath(this NavigationManager nav)
        => nav.ToBaseRelativePath(nav.Uri);
}
