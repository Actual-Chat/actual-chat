using Blazored.Modal;
using Blazored.Modal.Services;
using Stl.Extensibility;

namespace ActualChat.UI.Blazor.Services;

public sealed class ModalUI
{
    private ModalService ModalService { get; }
    private IMatchingTypeFinder MatchingTypeFinder { get; }

    public ModalUI(ModalService modalService, IMatchingTypeFinder matchingTypeFinder)
    {
        ModalService = modalService;
        MatchingTypeFinder = matchingTypeFinder;
    }

#pragma warning disable IL2072
    public IModalReference Show<TModel>(TModel model, bool isFullScreen = false)
        where TModel : class
    {
        var componentType = MatchingTypeFinder.TryFind(model.GetType(), typeof(IModalView));
        if (componentType == null)
            throw StandardError.NotFound<IModalView>(
                $"No modal view component for '{model.GetType()}' model.");

        var modalOptions = new ModalOptions {
            Class = $"blazored-modal modal"
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
