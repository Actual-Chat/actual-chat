namespace ActualChat.UI.Blazor.Services;

public class ToastUI
{
    private readonly MutableList<ToastModel> _items = new();

    public IReadOnlyMutableList<ToastModel> Items => _items;

    public void Show(string info, ToastDismissDelay autoDismissDelay)
        => ShowInternal(info, "", null, "", autoDismissDelay);

    public void Show(string info, string icon, ToastDismissDelay autoDismissDelay)
        => ShowInternal(info, icon, null, "", autoDismissDelay);

    public void Show(string info, Action action, string actionText, ToastDismissDelay autoDismissDelay)
        => ShowInternal(info, "", action, actionText, autoDismissDelay);

    public void Show(string info, string icon, Action action, string actionText, ToastDismissDelay autoDismissDelay)
        => ShowInternal(info, icon, action, actionText, autoDismissDelay);

    public bool Dismiss(ToastModel toast)
        => _items.Remove(toast);

    // Private methods

    private void ShowInternal(string info, string icon, Action? action, string actionText, ToastDismissDelay autoDismissDelay)
        => _items.Add(new ToastModel(info, icon, action, actionText, GetDelay(autoDismissDelay)));

    private static double? GetDelay(ToastDismissDelay autoDismissDelay)
        => autoDismissDelay switch {
            ToastDismissDelay.Short => 3,
            _ => 5
        };
}
