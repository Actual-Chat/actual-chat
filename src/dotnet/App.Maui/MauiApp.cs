using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui;

public class MauiApp : UI.Blazor.App.App
{
    [Inject] private NavigationManager Nav { get; init; } = null!;
    [Inject] private NavigationInterceptor NavInterceptor { get; init; } = null!;

    protected override void OnInitialized()
        => NavInterceptor.Initialize(Nav);
}
