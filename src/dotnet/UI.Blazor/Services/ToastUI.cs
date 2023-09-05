namespace ActualChat.UI.Blazor.Services;

public enum ToastDismissDelay { Short, Long }

public class ToastUI
{
    private readonly MutableList<InfoToastModel> _items = new ();

    public IReadOnlyMutableList<InfoToastModel> Items => _items;

    public void Show(string info, ToastDismissDelay autoDismissDelay)
        => ShowInternal(info, "", null, "", autoDismissDelay);

    public void Show(string info, string icon, ToastDismissDelay autoDismissDelay)
        => ShowInternal(info, icon, null, "", autoDismissDelay);

    public void Show(string info, Action action, string actionText, ToastDismissDelay autoDismissDelay)
        => ShowInternal(info, "", action, actionText, autoDismissDelay);

    public void Show(string info, string icon, Action action, string actionText, ToastDismissDelay autoDismissDelay)
        => ShowInternal(info, icon, action, actionText, autoDismissDelay);

    public bool Dismiss(InfoToastModel toastInfo)
        => _items.Remove(toastInfo);

    private void ShowInternal(string info, string icon, Action? action, string actionText, ToastDismissDelay autoDismissDelay)
        => _items.Add(new InfoToastModel(info, icon, action, actionText, GetDelay(autoDismissDelay)));

    private double? GetDelay(ToastDismissDelay autoDismissDelay)
        => autoDismissDelay switch {
            ToastDismissDelay.Short => 3,
            _ => 5
        };
}

// Must be class, not record, to avoid issue with blazor @key comparison.
public class InfoToastModel
{
    public string Info { get; }
    public string Icon { get; }
    public string ActionText { get;  }
    public Action? Action { get; }
    public double? AutoDismissDelay { get; }

    public InfoToastModel(string info, string icon, Action? action, string actionText, double? autoDismissDelay)
    {
        Info = info;
        Icon = icon;
        ActionText = actionText;
        Action = action;
        AutoDismissDelay = autoDismissDelay;
    }
};
