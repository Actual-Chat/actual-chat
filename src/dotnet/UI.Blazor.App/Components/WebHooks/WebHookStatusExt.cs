using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using ActualChat.WebHooks;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

public static class WebHookStatusExt
{
    public static string GetHost(this WebHook hook)
        => Uri.TryCreate(hook.Url, UriKind.Absolute, out var uri) ? uri.Host : hook.Url;

    public static async Task<(string? Text, bool IsFailing)> GetStatusText(
        this WebHook hook,
        IStringLocalizer l,
        LiveTime liveTime,
        CancellationToken cancellationToken)
    {
        var activityText = hook.LastActivityAt is { } lastActivityAt
            ? await liveTime.GetDeltaText(lastActivityAt, cancellationToken).ConfigureAwait(false)
            : null;
        return hook switch {
            { DisabledReason: WebHookDisabledReason.DeliveryFailures } => (l.Integrations_DisabledAfterFailures, true),
            { DisabledReason: WebHookDisabledReason.UnsafeUrl } => (l.Integrations_DisabledUnsafeUrl, true),
            { ConsecutiveFailures: > 0 } when activityText != null
                => (l.Integrations_FailingSince_Format(activityText), true),
            _ when activityText != null => (l.Integrations_LastDelivery_Format(activityText), false),
            _ => (null, false),
        };
    }
}
