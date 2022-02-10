using Markdig.Helpers;
using Markdig.Syntax.Inlines;

namespace ActualChat.Chat;

internal class LinkInlineRenderer : MarkupObjectRenderer<LinkInline>
{
    private static readonly string[] _imageExtensions = new[] {".bmp", ".png", ".jpg"};

    protected override void Write(MarkupRenderer renderer, LinkInline link)
    {
        var url = (link.GetDynamicUrl != null ? link.GetDynamicUrl() ?? link.Url : link.Url) ?? "";

        LiteralInline? firstLiteral = null;
        LiteralInline? lastLiteral = null;
        foreach (var literal in link.OfType<LiteralInline>()) {
            firstLiteral ??= literal;
            lastLiteral = literal;
        }

        var range = new Range<int>();
        if (firstLiteral != null) {
            range = new Range<int>(firstLiteral.Content.Start, lastLiteral!.Content.End + 1);
        }

        var isImage = link.IsImage;
        if (!isImage && Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)) {
            if (uri.Segments.Length > 0) {
                var last = uri.Segments.Last();
                var extension = Path.GetExtension(last).ToLowerInvariant();
                if (_imageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    isImage = true;
            }
        }

        if (isImage) {
            renderer.Parts.Add(new ImagePart {
                Url = url,
                Markup = renderer.Markup,
                TextRange = range
            });
        }
        else {
            renderer.Parts.Add(new LinkPart {
                Url = url,
                Markup = renderer.Markup,
                TextRange = range
            });
        }
    }
}
