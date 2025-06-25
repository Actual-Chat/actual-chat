using System.Diagnostics.CodeAnalysis;
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
    public async Task ShouldTranslateWithoutContext(string targetLanguage, string text, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), "", cancellationToken);
        Out.WriteLine($"Translated text:\n {translated}");

        // assert
        translated.Should().BeSimilarTo(expected, minSimilarity);
    }

    [Theory]
    [InlineData("ru",
        ComplexText,
        "Hi Alice, can you send me some code?",
        """
        Привет, **Боб**! Это тестовое сообщение с блоком кода:
        ```
        var number = 5;
        ```
        В этом коде `number = 5`.
        """)]
    [InlineData("ru", "I saw a bank", "I was heading towards the river", "Я увидел берег")]
    [InlineData("ru", "I saw a bank", "I need to go to the bank to withdraw money", "Я увидел банк")]
    [InlineData("fr", "I saw a bank", "I was heading towards the river", "J'ai vu une rive")]
    [InlineData("fr", "I saw a bank", "I need to go to the bank to withdraw money", "J'ai vu une banque")]
    public async Task ShouldTranslateWithContext(string targetLanguage, string text, string context, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), context, cancellationToken);
        Out.WriteLine($"Translated text: \n{translated}");

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
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), "", cancellationToken);

        // assert
        translated.Should().Be(Constants.Chat.NoTranslationNeededText);
    }

    [Theory]
    [InlineData("ru", "I", """
                           I was heading towards the river.
                           But it was still far away.
                           .
                           """, "Я")]
    public async Task ShouldNotTranslateContextForShortText(string targetLanguage, string text, string context, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(targetLanguage), context, cancellationToken);
        Out.WriteLine($"Translated text: \n{translated}");

        // assert
        translated.Should().BeSimilarTo(expected, minSimilarity);
    }
}
