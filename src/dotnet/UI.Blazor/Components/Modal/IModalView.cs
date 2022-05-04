namespace ActualChat.UI.Blazor.Components;

public interface IModalView
{
}

public interface IModalView<TModel> : IModalView
{
    TModel ModalModel { get; set; }
}
