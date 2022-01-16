namespace ActualChat.UI.Blazor.Components;

public record NavBarItem(Type ComponentType)
{
    public int Order { get; init; }

    public RenderFragment Content => (builder) => {
        builder.OpenComponent(0, ComponentType);
        builder.CloseComponent();
    };
}
