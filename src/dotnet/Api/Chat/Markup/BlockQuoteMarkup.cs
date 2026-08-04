using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// A Markdown-style block quote: one or more consecutive lines starting with <c>"&gt; "</c>.
/// Its inline content may contain any non-block markup (mentions, styles, urls, emoji, newlines).
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class BlockQuoteMarkup : BlockMarkup
{
    [DataMember, Key(0)]
    public Markup Content { get; }
    public BlockQuoteMarkup(Markup content)
    {
        if (content.IsBlockMarkup())
            throw new ArgumentException("Content must not be a block markup", nameof(content));

        Content = content;
    }

    public override string Format()
    {
        var content = Content.Format().Replace("\r\n", "\n");
        return "> " + content.Replace("\n", "\n> ");
    }

    public override Markup Simplify()
    {
        var simplified = Content.Simplify();
        return ReferenceEquals(simplified, Content) ? this : new BlockQuoteMarkup(simplified);
    }
}
