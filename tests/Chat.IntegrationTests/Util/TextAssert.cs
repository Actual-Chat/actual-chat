namespace ActualChat.Chat.IntegrationTests;

public class TextAssert
{
    public static void ShouldBeSimilar(string text, string expectedText, double minSimilarity)
    {
        var translatedWords = SplitIntoWords(text);
        var expectedWords = SplitIntoWords(expectedText);
        var intersectingWords = expectedWords.Intersect(expectedWords, StringComparer.OrdinalIgnoreCase).ToHashSet();
        var similarity = (double)intersectingWords.Count / Math.Max(translatedWords.Count, expectedWords.Count);
        similarity.Should().BeGreaterThanOrEqualTo(minSimilarity);
    }

    private static HashSet<string> SplitIntoWords(string text) =>
        text.Split([' ', ',', '!', '.', ':', '-'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
}
