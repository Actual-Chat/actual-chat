using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public sealed class MauiExternalUrlOpener : ExternalUrlOpener
{
    [method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiExternalUrlOpener))]
    public MauiExternalUrlOpener(UIHub hub) : base(hub) { }

    public override Task Open(string url)
        => MauiBrowser.Open(url);
}
