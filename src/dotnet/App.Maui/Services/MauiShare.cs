using ActualChat.UI.Blazor.Components;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of share functionality using platform-specific share dialogs.
/// </summary>
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiShare))]
public sealed class MauiShare(IServiceProvider services) : IMauiShare
{
    public Task Share(ShareRequest request)
    {
        var urlMapper = services.UrlMapper();
        var textAndLink = request.GetShareTextAndLink(urlMapper);
        if (textAndLink.IsNullOrEmpty())
            return Task.CompletedTask;

        var link = request.GetShareLink(urlMapper).NullIfEmpty();
        return Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(new ShareTextRequest {
            Title = request.Text,
            Text = textAndLink,
            Uri = link,
        });
    }
}
