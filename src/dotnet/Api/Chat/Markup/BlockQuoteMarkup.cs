using System.Text;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// A Markdown-style block quote: a run of consecutive lines starting with <c>"&gt; "</c>,
/// or with a bare <c>"&gt;"</c> for a blank one. Its content is a whole block document, so it
/// may hold headers, lists, code blocks, tables, and up to <see cref="MaxLevel"/> nested quotes.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class BlockQuoteMarkup(Markup content) : BlockMarkup
{
    public const int MaxLevel = 3;
    public const string Marker = ">";
    public const string Prefix = "> ";

    [DataMember, Key(0)]
    public Markup Content { get; } = content;

    public override string Format()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        AppendQuoted(Content.Format(), sb);
        return sb.ToStringAndRelease();
    }

    public override Markup Simplify()
    {
        var simplified = Content.Simplify();
        return ReferenceEquals(simplified, Content) ? this : new BlockQuoteMarkup(simplified);
    }

    // Protected/internal methods

    internal static void AppendQuoted(string content, StringBuilder sb)
    {
        // A blank line gets the bare marker - "> " adds trailing whitespace the parser strips off
        var newLine = NewLineMarkup.Instance.Text;
        var lines = content.NormalizeNewLines("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            if (i != 0)
                sb.Append(newLine);
            var line = lines[i];
            sb.Append(line.Length == 0 ? Marker : Prefix);
            sb.Append(line);
        }
    }
}
