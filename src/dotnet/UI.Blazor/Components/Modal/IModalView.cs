namespace ActualChat.UI.Blazor.Components;

public interface IModalView
{ }

public interface IModalView<TModel> : IModalView
    where TModel : class
{
    TModel ModalModel { get; set; }
}

public interface IOptionallyClosable
{
    bool CanClose { get; }
}
