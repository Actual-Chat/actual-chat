using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor.Components;

public interface IModalView
{ }

public interface IModalView<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]TModel> : IModalView
    where TModel : class
{
    TModel ModalModel { get; set; }
}

public interface IOptionallyClosable
{
    bool CanBeClosed { get; }
}
