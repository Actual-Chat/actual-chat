namespace ActualChat.UI.Blazor.App;

public sealed class AppRouter : Router
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_navigationInterceptionEnabled")]
    private static extern ref bool GetNavigationInterceptionEnabledField(Router @this);

    public AppRouter()
    {
        // Prevents second invocation of NavigationInterception.EnableNavigationInterceptionAsync.
        // The first time we do it manually from AppBase.OnInitializedAsync.
        // See comments in AppBase.OnInitializedAsync for details.
        ref bool f = ref GetNavigationInterceptionEnabledField(this);
        f = true;
    }
}
