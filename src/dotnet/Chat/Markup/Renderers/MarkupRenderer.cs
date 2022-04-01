using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ActualChat.Chat;

/// <summary>
/// A Markup based <see cref="IMarkdownRenderer"/>.
/// </summary>
/// <seealso cref="RendererBase" />
internal class MarkupRenderer : RendererBase
{
    private readonly MarkupProto _markupProto;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkupRenderer"/> class.
    /// </summary>
    /// <param name="markupProto">The markup.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public MarkupRenderer(MarkupProto markupProto)
    {
        this._markupProto = markupProto ?? throw new ArgumentNullException(nameof(markupProto));

        // // Default block renderers
        ObjectRenderers.Add(new CodeBlockRenderer());
        // ObjectRenderers.Add(new ListRenderer());
        // ObjectRenderers.Add(new HeadingRenderer());
        // ObjectRenderers.Add(new HtmlBlockRenderer());
        ObjectRenderers.Add(new ParagraphRenderer());
        // ObjectRenderers.Add(new QuoteBlockRenderer());
        // ObjectRenderers.Add(new ThematicBreakRenderer());
        //
        // // Default inline renderers
        // ObjectRenderers.Add(new AutolinkInlineRenderer());
        ObjectRenderers.Add(new CodeInlineRenderer());
        // ObjectRenderers.Add(new DelimiterInlineRenderer());
        // ObjectRenderers.Add(new EmphasisInlineRenderer());
        // ObjectRenderers.Add(new LineBreakInlineRenderer());
        // ObjectRenderers.Add(new HtmlInlineRenderer());
        // ObjectRenderers.Add(new HtmlEntityInlineRenderer());
        ObjectRenderers.Add(new LinkInlineRenderer());
        ObjectRenderers.Add(new LiteralInlineRenderer());
    }

    public List<MarkupPart> Parts
        => _markupProto.Parts;

    public Markup Markup
        => _markupProto.Markup;

    /// <summary>
    /// Renders the specified markdown object (returns the <see cref="MarkupProto"/> as a render object).
    /// </summary>
    /// <param name="markdownObject">The markdown object.</param>
    /// <returns></returns>
    public override object Render(MarkdownObject markdownObject)
    {
        Write(markdownObject);
        return _markupProto;
    }

    /// <summary>
    /// Writes the inlines of a leaf inline.
    /// </summary>
    /// <param name="leafBlock">The leaf block.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MarkupRenderer WriteLeafInline(LeafBlock leafBlock)
    {
        if (leafBlock is null) throw new ArgumentNullException(nameof(leafBlock));
        var inline = (Inline)leafBlock.Inline!;

        while (inline != null)
        {
            Write(inline);
            inline = inline.NextSibling;
        }

        return this;
    }
}
