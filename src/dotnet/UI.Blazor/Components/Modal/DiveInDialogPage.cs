using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor.Components;

public class DiveInDialogPage(Type componentType, object? model)
{
    public static DiveInDialogPage New<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>
        (object? model = null)
        where TComponent : IComponent
        => new (typeof(TComponent), model);

    public Type ComponentType { get; } = componentType;

    public object? Model { get; } = model;
}
