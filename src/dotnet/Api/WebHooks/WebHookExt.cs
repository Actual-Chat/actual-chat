namespace ActualChat.WebHooks;

public static class WebHookExt
{
    public static WebHook ApplyDiff(this WebHook webHook, WebHookDiff diff)
        => webHook with {
            Name = diff.Name?.Trim() ?? webHook.Name,
            Url = diff.Url?.Trim() ?? webHook.Url,
            Events = diff.Events ?? webHook.Events,
            IncludeText = diff.IncludeText ?? webHook.IncludeText,
            ChatIds = diff.ChatIds ?? webHook.ChatIds,
            SubscribeNotifications = diff.SubscribeNotifications ?? webHook.SubscribeNotifications,
            CustomHeaderName = diff.CustomHeaderName is { } headerName
                ? headerName.Trim().NullIfEmpty()
                : webHook.CustomHeaderName,
            IsEnabled = diff.IsEnabled ?? webHook.IsEnabled,
        };
}
