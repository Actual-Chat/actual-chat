namespace ActualChat.UI.Blazor;

public struct BlazorAttributesBuilder
{
    public IReadOnlyDictionary<string, object>? Result { get; private set; }

    public static BlazorAttributesBuilder New(IReadOnlyDictionary<string, object>? additionalAttributes = null)
        => new (additionalAttributes);

    private BlazorAttributesBuilder(IReadOnlyDictionary<string, object>? attributes)
        => Result = attributes;

#pragma warning disable CA1030 // Consider making 'AddOnClick' an event
    public BlazorAttributesBuilder AddOnClick(object receiver, EventCallback click)
    {
        if (click.HasDelegate)
            AddAttribute("onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver, click));
        return this;
    }

    public BlazorAttributesBuilder AddOnClick(object receiver, EventCallback<MouseEventArgs> click)
    {
        if (click.HasDelegate)
            AddAttribute("onclick", EventCallback.Factory.Create(receiver, click));
        return this;
    }
#pragma warning restore CA1030 // Consider making 'AddOnClick' an event

    private void AddAttribute(string attributeName, object attributeValue)
    {
        var temp = Result as ImmutableDictionary<string, object>
            ?? (Result != null
            ? ImmutableDictionary.CreateRange(Result)
            : ImmutableDictionary<string, object>.Empty);
        Result = temp.Add(attributeName, attributeValue);
    }
}
