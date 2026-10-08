using System.Text;
using ActualChat.Chat;

namespace ActualChat.Text;

public static class LoremIpsum
{
    private static readonly string[] Sentences = [
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
        "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.",
        "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore.",
        "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia.",
        "Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut fugit.",
        "Neque porro quisquam est, qui dolorem ipsum quia dolor sit amet.",
        "Ut enim ad minima veniam, quis nostrum exercitationem ullam corporis suscipit.",
        "Quis autem vel eum iure reprehenderit qui in ea voluptate velit esse.",
        "At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis.",
        "Nam libero tempore, cum soluta nobis est eligendi optio cumque nihil impedit.",
        "Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe.",
        "Itaque earum rerum hic tenetur a sapiente delectus, ut aut reiciendis.",
        "Nulla pariatur excepteur sint occaecat cupidatat non proident deserunt mollit.",
        "Curabitur pretium tincidunt lacus sed porttitor lectus nibh vulputate.",
        "Fusce dapibus, tellus ac cursus commodo, tortor mauris condimentum nibh.",
        "Donec id elit non mi porta gravida at eget metus vestibulum.",
        "Praesent commodo cursus magna, vel scelerisque nisl consectetur et viverra.",
        "Maecenas sed diam eget risus varius blandit sit amet non magna.",
        "Cras mattis consectetur purus sit amet fermentum aenean lacinia bibendum.",
        "Integer posuere erat a ante venenatis dapibus posuere velit aliquet.",
        "Vivamus sagittis lacus vel augue laoreet rutrum faucibus dolor auctor.",
        "Morbi leo risus, porta ac consectetur ac, vestibulum at eros donec.",
        "Aenean eu leo quam pellentesque ornare sem lacinia quam venenatis.",
        "Nullam quis risus eget urna mollis ornare vel eu leo praesent.",
    ];

    private static readonly string[] Words = Sentences
        .SelectMany(x => x.Split(' '))
        .Select(x => x.Trim('.', ','))
        .Where(x => x.Length > 2)
        .Distinct()
        .ToArray();

    // One of each inline markup element
    private static readonly Func<string>[] Decorations = [
        () => $"**{Phrase(1, 2)}**",
        () => $"*{Phrase(1, 2)}*",
        () => $"***{Word()}***",
        () => $"||{Phrase(1, 3)}||",
        () => $"`{Word()}_{Word()}`",
        () => $"#{Word()}",
        () => $"[{Phrase(1, 3)}](https://example.com/{Word()})",
        () => $"<https://example.com/{Word()}>",
        () => $"https://example.com/{Word()}",
        () => $"www.{Word()}.example.com",
        () => $"{Word()}@example.com",
    ];

    private static readonly (string Language, string[] Lines)[] CodeSnippets = [
        ("cs", ["var answer = 42;", "Console.WriteLine(answer);", "await Task.Delay(100);"]),
        ("ts", ["const answer: number = 42;", "console.log(answer);", "await sleep(100);"]),
        ("json", ["{", "  \"id\": 42,", "  \"name\": \"lorem\"", "}"]),
        ("sql", ["select id, name", "from chats", "where id = 42;"]),
        ("bash", ["echo hello", "ls -la", "git status"]),
    ];

    // Every block kind once, in a random order, then random ones: whatever the length, the text keeps mixing
    private static readonly Func<string>[] GuaranteedBlocks = [
        () => HeaderBlock(1), () => HeaderBlock(2), () => HeaderBlock(3),
        AllInlineBlock, ListBlock, () => QuoteBlock(true), TableBlock, CodeBlock, DividerBlock,
    ];

    private static readonly Func<string>[] RandomBlocks = [
        () => HeaderBlock(Random.Shared.Next(1, 4)), ParagraphBlock, ListBlock,
        () => QuoteBlock(Random.Shared.Next(2) == 0), TableBlock, CodeBlock, DividerBlock, AllInlineBlock,
    ];

    public static string GetRandomSentence()
        => Sentences[Random.Shared.Next(Sentences.Length)];

    // Random markup, for eyeballing how a stream renders: every kind of block and inline element,
    // from the first line to the last, cut to exactly `length` chars - wherever that lands.
    public static string GetMarkupSample(int length, bool mustIncludeReset)
    {
        var sb = new StringBuilder();
        foreach (var block in GuaranteedBlocks.OrderBy(_ => Random.Shared.Next()))
            sb.Append(block.Invoke()).Append("\n\n");
        while (sb.Length < length)
            sb.Append(Pick(RandomBlocks).Invoke()).Append("\n\n");

        var text = sb.ToString(0, length);
        return mustIncludeReset ? InsertReset(text) : text;
    }

    // Chunks of the text with the delay to wait before sending each: about `charsPerSecond` overall,
    // in about `updatesPerSecond` updates a second, each interval anywhere from half to one and a half
    // times the average one, so the chunks vary in both size and timing.
    public static IEnumerable<(string Chunk, TimeSpan Delay)> GetStreamPlan(
        string text, double charsPerSecond, double updatesPerSecond)
    {
        var position = 0;
        var delaySeconds = 0d;
        var chars = 0d;
        while (position < text.Length) {
            var intervalSeconds = (0.5 + Random.Shared.NextDouble()) / updatesPerSecond;
            delaySeconds += intervalSeconds;
            chars += charsPerSecond * intervalSeconds;
            var length = Math.Min((int)chars, text.Length - position);
            if (length == 0)
                continue;

            var chunk = text.Substring(position, length);
            var delay = TimeSpan.FromSeconds(delaySeconds);
            position += length;
            chars -= length;
            delaySeconds = 0;
            yield return (chunk, delay);
        }
    }

    // Private methods

    private static string HeaderBlock(int level)
        => $"{new string('#', level)} {Phrase(2, 5)}";

    private static string ParagraphBlock()
        => string.Join('\n', Enumerable.Range(0, Random.Shared.Next(1, 4)).Select(_ => Inline()));

    private static string AllInlineBlock()
        => string.Join(' ', Decorations
            .OrderBy(_ => Random.Shared.Next())
            .Select(x => $"{Phrase(1, 2)} {x.Invoke()}"));

    private static string ListBlock()
        => string.Join('\n', Enumerable.Range(0, Random.Shared.Next(2, 5)).Select(_ => $"- {Inline()}"));

    private static string QuoteBlock(bool isNested)
    {
        var lines = new List<string> { $"> {Inline()}" };
        if (isNested)
            lines.Add($"> > {Inline()}");
        lines.Add($"> {Inline()}");
        return string.Join('\n', lines);
    }

    private static string TableBlock()
    {
        var columnCount = Random.Shared.Next(2, 4);
        var alignments = new[] { "---", ":---", ":-:", "---:" };
        var header = Enumerable.Range(0, columnCount).Select(_ => Word());
        var delimiter = Enumerable.Range(0, columnCount).Select(_ => Pick(alignments));
        var lines = new List<string> { Row(header), Row(delimiter) };
        for (var i = Random.Shared.Next(1, 4); i > 0; i--)
            lines.Add(Row(Enumerable.Range(0, columnCount).Select(_ => Cell())));

        return string.Join('\n', lines);

        string Row(IEnumerable<string> cells)
            => $"| {string.Join(" | ", cells)} |";

        string Cell()
            => Random.Shared.Next(4) == 0 ? $"**{Word()}**" : Phrase(1, 2);
    }

    private static string CodeBlock()
    {
        var (language, lines) = Pick(CodeSnippets);
        var count = Random.Shared.Next(1, lines.Length + 1);
        return $"```{language}\n{string.Join('\n', lines.Take(count))}\n```";
    }

    private static string DividerBlock()
        => "---";

    // A sentence or two of plain words, some of them replaced by inline markup
    private static string Inline()
    {
        var parts = Enumerable.Range(0, Random.Shared.Next(2, 6))
            .Select(_ => Random.Shared.Next(3) == 0 ? Pick(Decorations).Invoke() : Phrase(2, 6));
        return string.Join(' ', parts);
    }

    private static string Phrase(int minWords, int maxWords)
        => string.Join(' ', Enumerable.Range(0, Random.Shared.Next(minWords, maxWords + 1)).Select(_ => Word()));

    private static string Word()
        => Pick(Words);

    private static T Pick<T>(T[] items)
        => items[Random.Shared.Next(items.Length)];

    // Between two lines, anywhere but inside a code block: there it would be code, not a reset
    private static string InsertReset(string text)
    {
        var lines = text.Split('\n').ToList();
        var candidates = new List<int>();
        var isInCodeBlock = false;
        for (var i = 0; i < lines.Count; i++) {
            if (!isInCodeBlock)
                candidates.Add(i);
            if (lines[i].StartsWith("```"))
                isInCodeBlock = !isInCodeBlock;
        }

        lines.Insert(candidates[Random.Shared.Next(candidates.Count)], MarkupParser.ResetMarker);
        return string.Join('\n', lines);
    }
}
