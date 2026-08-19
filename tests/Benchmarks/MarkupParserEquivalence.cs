using System.Text;
using ActualChat.Chat;

namespace ActualChat.Benchmarks;

/// <summary>
/// Compares <see cref="ParsecMarkupParser"/> against <see cref="MarkupParser"/> over a corpus that
/// covers every rule of the grammar. A speed number for a parser that doesn't produce the same tree
/// is worthless, so the benchmarks run this first and refuse to report anything if it disagrees.
/// </summary>
public static class MarkupParserEquivalence
{
    public static readonly string[] Corpus = [
        "",
        " ",
        "123 456",
        "123 _ 456",
        "Мороз и солнце; день\r\n чудесный!",
        "https://habr.com/ru/all/",
        "www.example.com:8080/path",
        "https://en.wikipedia.org/wiki/Sampling_(signal_processing)",
        "mail@example.com and mailto:mail@example.com",
        "@u:userId",
        "@`User Name`u:userId",
        "@`Name`a:c:1: text",
        "hi @u:userId, see @`Chat`c:chatId",
        "#promo and #tag-2 but not #4121 or c#5",
        "#a#b",
        "*italic* and **bold** and ||spoiler||",
        "**bold with *italic* inside**",
        "||spoiler with **bold**||",
        "**bold\nacross lines**",
        "`preformatted` text",
        "a ``b`` c",
        "**`a`/`b`** c",
        "- one\n- **`a`/`b`** two\n- three",
        "Hello **bold",
        "Hello ||spoi",
        "Hello `cod",
        "```\ncode\n```",
        "```csharp\nvar x = 1;\n\n    var y = 2;\n```",
        "```\nunclosed",
        "text\n```\ncode\n```\ntext",
        "# H1\n## H2\n### H3",
        "#### too many",
        "#nospace",
        "# Title with #tag and **bold**",
        "- a\n- b\n- c",
        "* a\n* b",
        "intro\n- a\n- b\n\nouttro",
        "> quote",
        "> a\n> b",
        "> hey @u:userId\n> see **this**",
        ">foo",
        "a\nb",
        "a\n\nb",
        "a\n\n\n\nb",
        "\n\n\na\n\n\n\nb\n\n\n",
        "| a | b |\n| --- | --- |\n| 1 | 2 |",
        "| a | b | c | d |\n| --- | :-- | :-: | --: |\n| 1 | 2 | 3 | 4 |",
        "| a | b |\n| --- | --- |",
        "|   a   |b|\n|---|---|",
        "| a | b\n| --- | ---\n| 1 | 2",
        "| a | b |\n| --- | --- |\n| 1 |",
        "| a | b |\n| --- | --- |\n| 1 | 2 | 3 |",
        "| a | b |\n| 1 | 2 |",
        "| a | b |\n| --- |",
        "| a |\n| x |",
        "||secret||",
        "| **b** | @u:userId |\n| --- | --- |\n| `code` | #tag |",
        "| a \\| b | c |\n| --- | --- |",
        "| a |\n| --- |\n| 1 |\n\ntext",
        "text\n| a |\n| --- |",
        "- | a | b |\n- | --- | --- |",
        "> | a | b |\n> | --- | --- |",
        "# H\n| a |\n| --- |\n## Next",
        "prefixes **`a`/`u`/`c`/`p`/`e`** (author)",
        "@`Frol`u:jo4vst32iijq ^ , that's it",
        "*",
        "**",
        "|",
        "`",
        "@",
        "a * b ** c || d",
    ];

    public static List<string> Verify(IEnumerable<string>? corpus = null)
    {
        var mismatches = new List<string>();
        var texts = (corpus ?? Corpus).Concat(
            Enum.GetValues<MarkupSampleKind>().Select(MarkupSamples.Get));
        foreach (var text in texts) {
            foreach (var useUnparsedTextMarkup in (bool[])[false, true]) {
                foreach (var allowIncompleteMarkup in (bool[])[false, true]) {
                    var expected = new MarkupParser {
                        UseUnparsedTextMarkup = useUnparsedTextMarkup,
                        AllowIncompleteMarkup = allowIncompleteMarkup,
                    }.Parse(text);
                    var actual = new ParsecMarkupParser {
                        UseUnparsedTextMarkup = useUnparsedTextMarkup,
                        AllowIncompleteMarkup = allowIncompleteMarkup,
                    }.Parse(text);
                    var expectedDump = Dump(expected);
                    var actualDump = Dump(actual);
                    if (expectedDump == actualDump)
                        continue;

                    var options = $"unparsed={useUnparsedTextMarkup}, incomplete={allowIncompleteMarkup}";
                    mismatches.Add($"""
                        Input ({options}): {Escape(Truncate(text))}
                          Pidgin: {Truncate(expectedDump)}
                          Parsec: {Truncate(actualDump)}
                        """);
                }
            }
        }

        return mismatches;
    }

    // Private methods

    private static string Dump(Markup markup)
    {
        var sb = new StringBuilder();
        Dump(markup, sb);
        return sb.ToString();
    }

    private static void Dump(Markup markup, StringBuilder sb)
    {
        switch (markup) {
        case MarkupSeq seq:
            DumpMany("Seq", seq.Items, sb);
            break;
        case ParagraphMarkup paragraph:
            DumpOne("Para", paragraph.Content, sb);
            break;
        case HeaderMarkup header:
            DumpOne($"H{header.Level}", header.Content, sb);
            break;
        case BlockQuoteMarkup blockQuote:
            DumpOne("Quote", blockQuote.Content, sb);
            break;
        case ListMarkup list:
            DumpMany("List", list.Items, sb);
            break;
        case ListItemMarkup listItem:
            DumpOne("Item", listItem.Content, sb);
            break;
        case TableMarkup table:
            sb.Append("Table[").AppendJoin(',', table.Alignments).Append(']');
            DumpMany("", [table.Header, .. table.Rows], sb);
            break;
        case TableRowMarkup row:
            DumpMany("Row", row.Cells, sb);
            break;
        case TableCellMarkup cell:
            DumpOne("Cell", cell.Content, sb);
            break;
        case StylizedMarkup stylized:
            DumpOne($"{stylized.Style}{(stylized.IsIncomplete ? "?" : "")}", stylized.Content, sb);
            break;
        case CodeBlockMarkup codeBlock:
            sb.Append("Code(").Append(codeBlock.Language).Append(':').Append(codeBlock.Code).Append(')');
            break;
        case MentionMarkup mention:
            sb.Append(mention.GetType().Name).Append('(').Append(mention.Format()).Append(')');
            break;
        default:
            sb.Append(markup.GetType().Name);
            if (markup.IsIncomplete)
                sb.Append('?');
            sb.Append('\'').Append(markup.Format()).Append('\'');
            break;
        }
    }

    private static void DumpOne(string name, Markup content, StringBuilder sb)
    {
        sb.Append(name).Append('(');
        Dump(content, sb);
        sb.Append(')');
    }

    private static void DumpMany(string name, IReadOnlyList<Markup> items, StringBuilder sb)
    {
        sb.Append(name).Append('(');
        for (var i = 0; i < items.Count; i++) {
            if (i != 0)
                sb.Append(',');
            Dump(items[i], sb);
        }
        sb.Append(')');
    }

    private static string Truncate(string text)
        => text.Length <= 160 ? text : text[..160] + "…";

    private static string Escape(string text)
        => text.Replace("\r", "\\r").Replace("\n", "\\n");
}
