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

    extension<TAssertions>(StringAssertions<TAssertions> assertions) where TAssertions : StringAssertions<TAssertions>
    {
        public AndConstraint<TAssertions> BeSimilarTo(
            string expected,
            double minSimilarity,
            [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string because = "",
            params object[] becauseArgs)
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
                    "Expected text {0} to be similar to {1} with min similarity {2} but actual similarity is {3}{reason}",
                    text,
                    expected,
                    minSimilarity,
                    similarity);
            return new AndConstraint<TAssertions>((TAssertions)assertions);
        }

        public AndConstraint<TAssertions> ContainWord(
            string expected,
            [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string because = "",
            params object[] becauseArgs)
        {
            var text = assertions.Subject;
            var words = text.SplitIntoWords().Select(Stem).ToList();
            var expectedStemmedWord = Stem(expected);
            assertions.CurrentAssertionChain.BecauseOf(because, becauseArgs)
                .ForCondition(words.Contains(expectedStemmedWord, StringComparer.OrdinalIgnoreCase))
                .FailWith("Expected text {0} to contain word {1}{reason} but it does not", text, expected);
            return new AndConstraint<TAssertions>((TAssertions)assertions);
        }
    }

    private static string Stem(string text)
        => text.ToLower().Replace("ё", "e", StringComparison.OrdinalIgnoreCase).TrimFirstFoundPrefix().TrimFirstFoundSuffix();

    private static string TrimFirstFoundPrefix(this string source)
    {
        foreach (var prefix in Prefixes)
            if (source.HasPrefix(prefix, StringComparison.OrdinalIgnoreCase, out var suffix)  && suffix.Length > 2)
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
