using Blazored.Modal;
using Blazored.Modal.Services;
using Stl.Extensibility;

namespace ActualChat.UI.Blazor.Services;

public sealed class ModalUI
{
    private readonly IModalService _modalService;
    private readonly IMatchingTypeFinder _matchingTypeFinder;

    public ModalUI(IModalService modalService, IMatchingTypeFinder matchingTypeFinder)
    {
        _modalService = modalService;
        _matchingTypeFinder = matchingTypeFinder;
    }

    public IModalReference Show<TModel>(TModel model)
        where TModel : class
    {
        var componentType = _matchingTypeFinder.TryFind(model.GetType(), typeof(IModalView));
        if (componentType == null)
            throw StandardError.NotFound<IModalView>(
                $"No modal view component for '{model.GetType()}' model.");

        var modalOptions = new ModalOptions {
            Class = "blazored-modal modal",
            HideHeader = true,
        };
        var modalParameters = new ModalParameters();
        modalParameters.Add(nameof(IModalView<TModel>.ModalModel), model);
        return _modalService.Show(componentType, "", modalParameters, modalOptions);
    }
}
