using System.Diagnostics.CodeAnalysis;
using Blazored.Modal;
using Blazored.Modal.Services;
using Stl.Extensibility;

namespace ActualChat.UI.Blazor.Services;

public sealed class ModalUI
{
    private BrowserInfo BrowserInfo { get; }
    private HistoryUI HistoryUI { get; }
    private ModalService ModalService { get; }
    private IMatchingTypeFinder MatchingTypeFinder { get; }

    public ModalUI(BrowserInfo browserInfo, HistoryUI historyUI, ModalService modalService, IMatchingTypeFinder matchingTypeFinder)
    {
        BrowserInfo = browserInfo;
        HistoryUI = historyUI;
        ModalService = modalService;
        MatchingTypeFinder = matchingTypeFinder;
    }

#pragma warning disable IL2072
    public Task<IModalRef> Show<TModel>(TModel model, bool isFullScreen = false)
        where TModel : class
    {
        var componentType = MatchingTypeFinder.TryFind(model.GetType(), typeof(IModalView));
        if (componentType == null)
            throw StandardError.NotFound<IModalView>(
                $"No modal view component for '{model.GetType()}' model.");

        if (!BrowserInfo.ScreenSize.Value.IsNarrow())
            return Task.FromResult(ShowInternal(componentType, model, isFullScreen));

        IModalRef? modalReference = null;
        var whenCompletedSource = TaskSource.New<IModalRef>(true);
        HistoryUI.NavigateTo(
            () => {
                modalReference = ShowInternal(componentType, model, isFullScreen);
                whenCompletedSource.TrySetResult(modalReference);
                modalReference.ModalCloseRequest += (s, e) => {
                    e.Handled = true;
                    _ = HistoryUI.GoBack();
                };
            },
            () => {
                modalReference?.Close();
            });
        return whenCompletedSource.Task;
    }

    private IModalRef ShowInternal<TModel>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType,
        TModel model,
        bool isFullScreen)
        where TModel : class
    {
        var modalOptions = new ModalOptions {
            Class = "blazored-modal modal",
        };
        if (isFullScreen)
            modalOptions.PositionCustomClass = "position-fullscreen";
        var modalContent = new RenderFragment(builder => {
            builder.OpenComponent(0, componentType);
            builder.AddAttribute(1, nameof(IModalView<TModel>.ModalModel), model);
            builder.CloseComponent();
        });
        return ModalService.Show(modalContent, modalOptions);
    }
#pragma warning restore IL2072
}
