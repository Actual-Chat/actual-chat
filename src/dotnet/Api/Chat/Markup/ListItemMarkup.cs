using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a single item in a <see cref="ListMarkup"/>.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class ListItemMarkup(Markup content) : Markup
{
    public const string Prefix = "- ";
    [DataMember, Key(0)]
    public Markup Content { get; } = content;

    public override string Format()
        => Prefix + Content.Format();

    public override Markup Simplify()
    {
        var content2 = Content.Simplify();
        if (ReferenceEquals(content2, Content))
            return this;

        return new ListItemMarkup(content2);
    }
}
