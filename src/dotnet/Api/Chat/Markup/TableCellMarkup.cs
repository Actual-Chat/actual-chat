using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// A single cell of a <see cref="TableRowMarkup"/>. Its content is inline markup only.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class TableCellMarkup : Markup
{
    [DataMember, Key(0)]
    public Markup Content { get; }

    public TableCellMarkup(Markup content)
    {
        if (content.IsBlockMarkup())
            throw new ArgumentException("Content must not be a block markup", nameof(content));

        Content = content;
    }

    public override string Format()
        => TableMarkup.EscapeCellText(Content.Format());

    public override Markup Simplify()
    {
        var content = Content.Simplify();
        return ReferenceEquals(content, Content) ? this : new TableCellMarkup(content);
    }
}
