namespace ActualChat.UI.Blazor.Components;

public class DiveInDialogPage([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]Type componentType, object? model)
{
    public static DiveInDialogPage New<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>
        (object? model = null)
        where TComponent : IComponent
        => new (typeof(TComponent), model);

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type ComponentType { get; } = componentType;

    public object? Model { get; } = model;
}
