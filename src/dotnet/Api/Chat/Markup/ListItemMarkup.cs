using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ListItemMarkup(Markup content, int? order = null) : Markup
{
    public Markup Content { get; } = content;
    public int? Order { get; } = order;

    public override string Format()
        => GetPrefix() + Content.Format();

    public string GetPrefix()
        => Order.HasValue ? $"{Order}. " : "- ";
}
