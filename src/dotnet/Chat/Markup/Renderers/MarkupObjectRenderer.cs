using Markdig.Renderers;
using Markdig.Syntax;

namespace ActualChat.Chat;

/// <summary>
/// A base class for HTML rendering <see cref="Block"/> and <see cref="Inlines.Inline"/> Markdown objects.
/// </summary>
/// <typeparam name="TObject">The type of the object.</typeparam>
/// <seealso cref="IMarkdownObjectRenderer" />
internal abstract class MarkupObjectRenderer<TObject> : MarkdownObjectRenderer<MarkupRenderer, TObject> where TObject : MarkdownObject
{
}
