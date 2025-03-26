namespace ActualChat.Chat.IntegrationTests;

public class TextAssert
{
    public static void ShouldBeSimilar(string text, string expectedText, double minSimilarity)
    {
        var translatedWords = text.SplitIntoWords();
        var expectedWords = expectedText.SplitIntoWords();
        var intersectingWords = expectedWords.Intersect(translatedWords, StringComparer.OrdinalIgnoreCase).ToHashSet();
        var similarity = (double)intersectingWords.Count / Math.Max(translatedWords.Length, expectedWords.Length);
        similarity.Should().BeGreaterThanOrEqualTo(minSimilarity);
    }
}
