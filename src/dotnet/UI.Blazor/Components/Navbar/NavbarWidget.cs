namespace ActualChat.UI.Blazor.Components;

public record NavbarWidget(Type ComponentType)
{
    public double Order { get; init; }
    public string? NavbarGroupId { get; init; }

    public RenderFragment Content => builder => {
        builder.OpenComponent(0, ComponentType);
        builder.CloseComponent();
    };
}
