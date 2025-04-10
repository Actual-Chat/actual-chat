using System.Diagnostics.CodeAnalysis;
using FluentAssertions.Primitives;

namespace ActualChat.Testing.Host.Assertion;

public static class StringAssertionsExt
{
    private static readonly string[] Endings = [
        "иями", "ями", "ами", "ими", "ией", "ей", "ий", "ый", "ой", "ем", "им", "ым", "ом", "у", "ю", "а", "я", "о",
        "е", "и", "ы", "ь", "н", "л", "ён",
    ];

    public static AndConstraint<TAssertions> BeSimilarTo<TAssertions>(
        this StringAssertions<TAssertions> assertions,
        string expected,
        double minSimilarity,
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string because = "",
        params object[] becauseArgs)
        where TAssertions : StringAssertions<TAssertions>
    {
        var text = assertions.Subject;
        var words = text.SplitIntoWords().Select(Stem).ToList();
        var expectedWords = expected.SplitIntoWords().Select(Stem).ToList();
        var intersectingWords = expectedWords.Intersect(words, StringComparer.OrdinalIgnoreCase).ToHashSet();
        var similarity = (double)intersectingWords.Count / Math.Max(words.Count, expectedWords.Count);
        assertions.CurrentAssertionChain.BecauseOf(because, becauseArgs)
            .ForCondition(similarity >= minSimilarity)
            .FailWith(
                "Expected text {0} to be similar to {1} with min similarity {2} but actual similarity is {3}",
                text,
                expected,
                minSimilarity,
                similarity);
        return new AndConstraint<TAssertions>((TAssertions)assertions);
    }

    public static AndConstraint<TAssertions> ContainWord<TAssertions>(
        this StringAssertions<TAssertions> assertions,
        string expected,
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string because = "",
        params object[] becauseArgs)
        where TAssertions : StringAssertions<TAssertions>
    {
        var text = assertions.Subject;
        var words = text.SplitIntoWords().Select(Stem).ToList();
        var expectedStemmedWord = Stem(expected);
        assertions.CurrentAssertionChain.BecauseOf(because, becauseArgs)
            .ForCondition(words.Contains(expectedStemmedWord, StringComparer.OrdinalIgnoreCase))
            .FailWith("text {0} does not contain word {1}", text, expected);
        return new AndConstraint<TAssertions>((TAssertions)assertions);
    }

    private static string Stem(string text)
        => text.ToLower().OrdinalIgnoreCaseReplace("ё", "e").TrimSuffixes(Endings);
}
