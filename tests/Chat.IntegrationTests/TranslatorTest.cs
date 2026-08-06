using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(TranslationCollection))]
[Trait("Category", "Nightly")]
public class TranslatorTest(TranslationCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TranslationCollection.AppHostFixture>(fixture, @out)
{
    private const string ComplexText = """
                                       Hello, **Bob**! This is a test message with a code block:
                                       ```
                                       var number = 5;
                                       ```
                                       In this code `number = 5`.
                                       """;
    private const string SpoilerText = "Don't read further: ||the murderer is the butler||. You have been warned!";
    private const string TwoSpoilersText = "The first answer is ||yes||, and the second one is ||no||.";

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
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
    public async Task ShouldTranslateWithoutContext(string targetLanguage, string text, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), [], cancellationToken);
        WriteLine($"Translated text:\n {translated}");

        // assert
        translated.Should().BeSimilarTo(expected, minSimilarity);
    }

    [Theory]
    [InlineData("ru",
        ComplexText,
        "Hi Alice, can you send me some code?",
        "Пожалуйста, пришли мне какой-нибудь код?",
        """
        Привет, **Боб**! Это тестовое сообщение с блоком кода:
        ```
        var number = 5;
        ```
        В этом коде `number = 5`.
        """)]
    public async Task ShouldTranslateWithContext(string targetLanguage, string text, string context, string translatedContext, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), [new TranslationResult( context, translatedContext)], cancellationToken);
        WriteLine($"Translated text: \n{translated}");

        // assert
        translated.Should().BeSimilarTo(expected, minSimilarity);
    }

    [Theory]
    [InlineData("ru", "I was heading towards the river", "Я направлялся к реке",
        new[] { "берег" }, new[] { "банк" })]
    [InlineData("ru", "I need to go to the bank to withdraw money", "Мне нужно пойти в банк, чтобы снять деньги.",
        new[] { "банк" }, new[] { "берег" })]
    [InlineData("fr", "I was heading towards the river", "Je me dirigeais vers la rivière",
        new[] { "rive", "berge", "rivage" }, new[] { "banque" })]
    [InlineData("fr", "I need to go to the bank to withdraw money", "Je dois aller à la banque pour retirer de l'argent",
        new[] { "banque" }, new[] { "rive", "berge", "rivage" })]
    public async Task ShouldUseContextToPickWordSense(
        string targetLanguage,
        string context,
        string translatedContext,
        string[] expectedSenseWords,
        string[] wrongSenseWords)
    {
        // "I saw a bank" is short enough that comparing the whole sentence flips on harmless phrasing
        // ("Я увидел берег" vs "Я увидел берег реки"); the only thing under test is which sense of
        // "bank" the context selects.
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;
        var context1 = new TranslationResult(context, translatedContext);

        // act
        var translated = await Translator.Translate(
            "I saw a bank", Language.Parse(targetLanguage), [context1], cancellationToken);
        WriteLine($"Translated text: \n{translated}");

        // assert
        translated.Should().ContainAnyWord(expectedSenseWords);
        translated.Should().NotContainAnyWord(wrongSenseWords);
    }

    [Theory]
    [InlineData("en", "hello")]
    [InlineData("en", "I saw a bank.")]
    public async Task ShouldSkipIfTextIsAlreadyInTargetLanguage(string targetLanguage, string text)
    {
        // arrange
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), [], cancellationToken);

        // assert
        translated.Should().Be(text);
    }

    [Theory]
    [InlineData("ru", "I", """
                           I was heading towards the river.
                           But it was still far away.
                           .
                           """,
                           """
                           Я направлялся к реке.
                           Но она всё ещё была далеко.
                           .
                           """,
        "Я")]
    public async Task ShouldNotTranslateContextForShortText(string targetLanguage, string text, string context, string translatedContext, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), [ new TranslationResult(context, translatedContext)], cancellationToken);
        WriteLine($"Translated text: \n{translated}");

        // assert
        translated.Should().BeSimilarTo(expected, minSimilarity);
    }

    [Theory]
    [InlineData("ru", SpoilerText, "Не читай дальше: ||убийца — дворецкий||. Ты был предупреждён!", "дворецкий")]
    [InlineData("fr", SpoilerText, "Ne lisez pas plus loin : ||le meurtrier est le majordome||. Vous êtes prévenu !", "majordome")]
    public async Task ShouldPreserveSpoiler(
        string targetLanguage,
        string text,
        string expected,
        string expectedSpoilerWord)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), [], cancellationToken);
        WriteLine($"Translated text:\n {translated}");

        // assert
        Unspoil(translated).Should().BeSimilarTo(Unspoil(expected), minSimilarity);
        var spoiler = GetSpoilers(translated).Should().ContainSingle().Subject;
        spoiler.Content.ToReadableText().Should().ContainWord(expectedSpoilerWord);
    }

    [Theory]
    [InlineData("ru", "||**Everyone** dies at the end||", "все")]
    [InlineData("fr", "||**Everyone** dies at the end||", "meurt")]
    public async Task ShouldPreserveMarkupInsideSpoiler(string targetLanguage, string text, string expectedSpoilerWord)
    {
        // arrange
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), [], cancellationToken);
        WriteLine($"Translated text:\n {translated}");

        // assert
        var spoiler = GetSpoilers(translated).Should().ContainSingle().Subject;
        spoiler.Content.Format().Should().Contain("**");
        spoiler.Content.ToReadableText().Should().ContainWord(expectedSpoilerWord);
    }

    [Theory]
    [InlineData("ru", TwoSpoilersText, "Первый ответ — ||да||, а второй — ||нет||.")]
    [InlineData("fr", TwoSpoilersText, "La première réponse est ||oui||, et la deuxième est ||non||.")]
    public async Task ShouldPreserveEverySpoiler(string targetLanguage, string text, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), [], cancellationToken);
        WriteLine($"Translated text:\n {translated}");

        // assert
        Unspoil(translated).Should().BeSimilarTo(Unspoil(expected), minSimilarity);
        GetSpoilers(translated).Should().HaveCount(GetSpoilers(text).Count);
    }

    // Private methods

    private static string Unspoil(string text)
        => text.Replace("||", "");

    private static List<StylizedMarkup> GetSpoilers(string text)
    {
        var spoilers = new List<StylizedMarkup>();
        Collect(new MarkupParser().Parse(text));
        return spoilers;

        void Collect(Markup markup) {
            switch (markup) {
            case StylizedMarkup { Style: TextStyle.Spoiler } spoiler:
                spoilers.Add(spoiler);
                break;
            case StylizedMarkup stylized:
                Collect(stylized.Content);
                break;
            case MarkupSeq seq:
                foreach (var item in seq.Items)
                    Collect(item);
                break;
            case ParagraphMarkup paragraph:
                Collect(paragraph.Content);
                break;
            case HeaderMarkup header:
                Collect(header.Content);
                break;
            case BlockQuoteMarkup blockQuote:
                Collect(blockQuote.Content);
                break;
            case ListMarkup list:
                foreach (var item in list.Items)
                    Collect(item);
                break;
            case ListItemMarkup listItem:
                Collect(listItem.Content);
                break;
            }
        }
    }
}
