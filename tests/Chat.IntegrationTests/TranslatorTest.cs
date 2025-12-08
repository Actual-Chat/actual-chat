using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(TranslationCollection))]
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
    [InlineData("ru", "I saw a bank", "I was heading towards the river", "Я направлялся к реке", "Я увидел берег")]
    [InlineData("ru", "I saw a bank", "I need to go to the bank to withdraw money", "Мне нужно пойти в банк, чтобы снять деньги.", "Я увидел банк")]
    [InlineData("fr", "I saw a bank", "I was heading towards the river", "Je me dirigeais vers la rivière", "J'ai vu une rive")]
    [InlineData("fr", "I saw a bank", "I need to go to the bank to withdraw money", "Je dois aller à la banque pour retirer de l'argent", "J'ai vu une banque")]
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
}
