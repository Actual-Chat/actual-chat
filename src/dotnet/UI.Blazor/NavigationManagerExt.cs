namespace ActualChat.UI.Blazor;

public static class NavigationManagerExt
{
    public static LocalUrl GetLocalUrl(this NavigationManager nav)
        => new(nav.ToBaseRelativePath(nav.Uri));

    public static void ExecuteOnSameLocationWithDelay(this NavigationManager nav, TimeSpan delay, Action action)
        => new NavigatorDelayedExecutor(nav).ExecuteAfter(delay, action);

    private class NavigatorDelayedExecutor
    {
        private readonly NavigationManager _nav;
        private bool _navigated;

        public void ExecuteAfter(TimeSpan delay, Action action)
            => _ = ExecuteAfterInternal(delay, action);

        private async Task ExecuteAfterInternal(TimeSpan delay, Action action)
        {
            await Task.Delay(delay).ConfigureAwait(true);
            if (_navigated)
                return;
            _nav.LocationChanged -= OnLocationChanged;
            action();
        }

        private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            _nav.LocationChanged -= OnLocationChanged;
            _navigated = true;
        }

        public NavigatorDelayedExecutor(NavigationManager nav)
        {
            _nav = nav;
            _nav.LocationChanged += OnLocationChanged;
        }
    }
}
