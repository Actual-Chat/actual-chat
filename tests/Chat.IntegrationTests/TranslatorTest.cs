using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class TranslatorTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string ComplexText = """
                                       Hello, **Bob**! This is a test message with a code block:
                                       ```
                                       var number = 5;
                                       ```
                                       In this code `number = 5`.
                                       """;

    [field: AllowNull, MaybeNull]
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    [field: AllowNull, MaybeNull]
    private Translator Translator => field ??= Tester.AppServices.GetRequiredService<Translator>();

    [Theory]
    [InlineData("ru",
        ComplexText,
        """
        Привет, **Боб**! Это тестовое сообщение с блоком кода:
        ```
        var number = 5;
        ```
        В этом коде `number = 5`.
        """)]
    [InlineData("it",
        ComplexText,
        """
        Ciao, **Bob**! Questo è un messaggio di prova con un blocco di codice:
        ```
        var number = 5;
        ```
        In questo codice `number = 5`.
        """)]
    [InlineData("fr",
        ComplexText,
        """
        Bonjour, **Bob** ! Ceci est un message de test avec un bloc de code :
        ```
        var number = 5;
        ```
        Dans ce code, `number = 5`.
        """)]
    [InlineData("it", "Поехали", "Andiamo!")]
    [InlineData("en", "Поехали", "Let's go")]
    public async Task ShouldTranslate(Language destLanguage, string text, string expected)
    {
        // arrange
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, destLanguage, cancellationToken);
        Out.WriteLine($"Translated text:\n {translated}");

        // assert
        ShouldBeSimilar(translated, expected);
    }

    [Theory(Skip = "flaky")] // TODO(FC): stabilize it
    [InlineData(ComplexText, "en")]
    [InlineData("Hello! Привет! Bonjour!", "en", "ru", "fr")]
    [InlineData("```")]
    [InlineData("````123```")]
    [InlineData("123")]
    [InlineData("0xDEADBEEF")]
    public async Task ShouldDetectLanguages(string text, params string[] expected)
    {
        // arrange
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var runCount = 10;
        var results = await Enumerable.Range(1, 10).Select(_ => Translator.DetectLanguages(text, cancellationToken)).Collect(cancellationToken);
        var stats = results.SelectMany(x => x).GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());

        // assert
        foreach (var language in expected)
            stats.GetValueOrDefault(language).Should().BeGreaterThanOrEqualTo((int)Math.Floor(0.7 * runCount));
    }

    private void ShouldBeSimilar(string translatedText, string expectedText)
    {
        var translatedWords = SplitIntoWords(translatedText);
        var expectedWords = SplitIntoWords(expectedText);
        var intersectingWords = expectedWords.Intersect(expectedWords, StringComparer.OrdinalIgnoreCase).ToHashSet();
        var similarity = (decimal)intersectingWords.Count / Math.Max(translatedWords.Count, expectedWords.Count);
        similarity.Should().BeGreaterThanOrEqualTo(0.7m);

        HashSet<string> SplitIntoWords(string text)
            => text.Split([' ', ',', '!', '.', ':', '-'],
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet();
    }
}
