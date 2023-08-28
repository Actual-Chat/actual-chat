namespace ActualChat.UI.Blazor;

public struct BlazorAttributesBuilder
{
    private IReadOnlyDictionary<string, object>? _attributes;

    private BlazorAttributesBuilder(IReadOnlyDictionary<string, object>? attributes)
        => _attributes = attributes;

    public static BlazorAttributesBuilder Create(IReadOnlyDictionary<string, object>? additionalAttributes = null)
        => new (additionalAttributes);

    public IReadOnlyDictionary<string, object>? GetResult()
        => _attributes;

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

    private void AddAttribute(string attributeName, object attributeValue)
    {
        var temp = _attributes as ImmutableDictionary<string, object>
            ?? (_attributes != null
            ? ImmutableDictionary.CreateRange(_attributes)
            : ImmutableDictionary<string, object>.Empty);
        _attributes = temp.Add(attributeName, attributeValue);
    }
}
