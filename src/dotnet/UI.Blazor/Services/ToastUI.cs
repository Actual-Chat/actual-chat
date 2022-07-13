using Blazored.Toast;
using Blazored.Toast.Services;

namespace ActualChat.UI.Blazor.Services;

public sealed class ToastUI
{
    private readonly IToastService _toastService;

    public ToastUI(IToastService toastService)
        => _toastService = toastService;

    public void ShowToast(ToastLevel level, string message, string heading = "", Action? onClick = null)
        => _toastService.ShowToast(level, message, heading, onClick);

    public void ShowToast(ToastLevel level, RenderFragment message, string heading = "", Action? onClick = null)
        => _toastService.ShowToast(level, message, heading, onClick);

    public void ShowToast<TComponent>(ToastParameters parameters, ToastInstanceSettings settings)
        where TComponent : IComponent
        => _toastService.ShowToast<TComponent>(parameters, settings);


    public void ClearToasts(ToastLevel level)
        => _toastService.ClearToasts(level);
}
