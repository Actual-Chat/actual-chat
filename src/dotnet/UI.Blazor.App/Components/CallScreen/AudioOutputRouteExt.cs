using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public static class AudioOutputRouteExt
{
    extension(AudioOutputRoute? route)
    {
        public string GetIcon()
            => route?.Kind switch {
                AudioOutputKind.Phone => "icon-phone",
                AudioOutputKind.Speaker => "icon-volume-up-2",
                AudioOutputKind.Car => "icon-car",
                _ => "icon-headphones-fill",
            };
    }
}
