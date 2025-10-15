using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions.Primitives;

namespace ActualChat.Testing.Host.Assertion;

public static class StringAssertionsExt
{
    private static readonly string[] Prefixes = ["у"];

    private static readonly string[] Suffixes = [
        "ами", "ими", "ел", "ен", "у", "ие", "ии", "ся", "е", "ы", "а",
    ];

    private static readonly (string Word, string SimilarWord)[] Synonyms = [
        ("требуется", "нуждается"),
        ("разрушается", "размывается"),
        ("уход", "обслуживание"),
    ];

    private static readonly Dictionary<string, string> SimilarWords =
        Synonyms.Concat(Synonyms.Select(x => (x.SimilarWord, x.Word)))
            .Select(x => (Stem(x.Item1), Stem(x.Item2)))
            .ToDictionary(x => x.Item1, x => x.Item2);

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
        intersectingWords.AddRange(words.Select(SimilarWords.GetValueOrDefault).SkipNullItems());
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
        => text.ToLower().OrdinalIgnoreCaseReplace("ё", "e").TrimFirstFoundPrefix().TrimFirstFoundSuffix();

    private static string TrimFirstFoundPrefix(this string source)
    {
        foreach (var prefix in Prefixes)
            if (source.OrdinalIgnoreCaseHasPrefix(prefix, out var suffix)  && suffix.Length > 2)
                return suffix;

        return source;
    }

    private static string TrimFirstFoundSuffix(this string source)
    {
        foreach (var suffix in Suffixes) {
            var trimmedText = source.TrimSuffix(suffix);
            if (!ReferenceEquals(trimmedText, source) && trimmedText.Length > 3)
                return trimmedText;
        }
        return source;
    }
}
