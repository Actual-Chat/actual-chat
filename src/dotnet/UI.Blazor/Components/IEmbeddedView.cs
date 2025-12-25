namespace ActualChat.UI.Blazor.Components;

public interface IEmbeddedView;

public interface IEmbeddedView<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]TModel> : IEmbeddedView
    where TModel : class
{
    TModel ViewModel { get; set; }
}
