using Blazored.Modal;
using Blazored.Modal.Services;
using Stl.Extensibility;

namespace ActualChat.UI.Blazor.Services;

public class ModalUI
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
            throw new InvalidOperationException($"Can not find matching modal view component for model '{model.GetType()}'");
        var modalOptions = new ModalOptions {
            HideHeader = true,
            Class = "blazored-modal custom-modal-class",
        };
        var modalParameters = new ModalParameters();
        modalParameters.Add(nameof(IModalView<TModel>.ModalModel), model);
        return _modalService.Show(componentType, "", modalParameters, modalOptions);
    }
}
