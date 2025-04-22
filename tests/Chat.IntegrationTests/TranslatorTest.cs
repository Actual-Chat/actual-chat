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
    public async Task ShouldTranslateWithoutContext(string destLanguage, string text, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(destLanguage), "", cancellationToken);
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
    public async Task ShouldTranslateWithContext(string destLanguage, string text, string context, string expected)
    {
        // arrange
        var minSimilarity = 0.7;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var translated = await Translator.Translate(text, Language.Parse(destLanguage), context, cancellationToken);
        Out.WriteLine($"Translated text: \n{translated}");

        // assert
        translated.Should().BeSimilarTo(expected, minSimilarity);
    }

    [Fact]
    public async Task ShouldDetectLanguages()
    {
        // arrange
        (string Text, Language?[] ExpectedLanguages)[] texts = [
            (ComplexText, [Languages.English]),
            ("Hello! Привет! Bonjour!", [Languages.English, Languages.Russian, Languages.French]),
            ("Hello! Привет! Bonjour! Привет!", [Languages.English, Languages.Russian, Languages.French]),
            ("hello", [Languages.English]),
            ("привет", [Languages.Russian]),
            ("```", []),
            ("````123```", []),
            ("123", []),
            ("0xDEADBEEF", []),
            ("Hello! @#$%^&*()_+", [Languages.English]),
        ];
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var languages = await Translator.DetectLanguages([..texts.Select(x => x.Text)], cancellationToken);

        // assert
        for (var i = 0; i < texts.Length; i++) {
            var expectedLanguages = texts[i].ExpectedLanguages;
            languages[i].Should().BeEquivalentTo(expectedLanguages, "for text: <<<{0}>>>", texts[i].Text);
        }
    }

    [Fact]
    public async Task ShouldDetectLanguagesForManyEntries()
    {
        // arrange
        (string Text, Language[] ExpectedLanguages)[] texts = [
            ("hello", [Languages.English]),
            ("До скорых.", [Languages.Russian]),
            ("Попытка", [Languages.Russian]),
            ("hello", [Languages.English]),
            ("привет", [Languages.Russian]),
            ("hello", [Languages.English]),
            ("hello", [Languages.English]),
            ("привет", [Languages.Russian]),
            ("What do you think about this unsat language?", [Languages.English]),
            ("Развод.", [Languages.Russian]),
            ("Probably that from there. Probably.", [Languages.English]),
            ("hello", [Languages.English]),
            ("I don't mind.", [Languages.English]),
            ("I'm a Google guy right now.", [Languages.English]),
            ("Нет, мы не били.", [Languages.Russian]),
            ("bonjour", [Languages.French]),
            ("или не надо", [Languages.Russian]),
            ("Давай попробуем", [Languages.Russian]),
            ("Раз два три, 4 раз два, три, 4, 5", [Languages.Russian]),
            ("Развод Мы тестируем где мы тестируем.", [Languages.Russian]),
            ("Joseph.", [Languages.English]),
            ("Merhaba", [Languages.Turkish]),
            ("Если что-нибудь попробую.", [Languages.Russian]),
            ("Я хочу что-нибудь попробовать катать.", [Languages.Russian]),
            ("Вязи хочу наговорить большой текст, чтобы у нас было понятно, какой текст транскрипируется, и Какой Что хранит VLOST Transcript?", [Languages.Russian]),
            ("Guten Tag.", [Languages.German]),
            ("Разворачиваем.", [Languages.Russian]),
            ("Давай.", [Languages.Russian]),
            ("Нет не серьёзно.", [Languages.Russian]),
            ("А так попробуй распо. Распознать", [Languages.Russian]),
            ("Попробуем", [Languages.Russian]),
            ("Я помнишь, как саголикон и ты тоже их майко.", [Languages.Russian]),
            ("parle français.", [Languages.French]),
            ("Hi.", [Languages.English]),
            ("The meta balance Good.", [Languages.English]),
            ("Но я моё!", [Languages.Russian]),
            ("Серьезно?", [Languages.Russian]),
            ("давай переводи", [Languages.Russian]),
            ("Что за хренотал?", [Languages.Russian]),
            ("Привет. Я должен осознать язык, точнее взять из конструкции то есть.", [Languages.Russian]),
            ("Я говорю", [Languages.Russian]),
            ("1 I don't mind this.", [Languages.English]),
            ("Я снова попробую наговорить текст. Я сейчас буду наговаривать текст.", [Languages.Russian]),
            ("Давай.", [Languages.Russian]),
            ("Одного.", [Languages.Russian]),
            ("Good and tag. Our feeder is in.", [Languages.English]),
            ("Мы тестируем дипграмму.", [Languages.Russian]),
            ("Пробуем записывать.", [Languages.Russian]),
            ("Аминь, айпидок! А.", [Languages.Russian]),
            ("Раз, два, три.", [Languages.Russian]),
            ("Так, ну как это давай попробуем. Потерять язык, который мы получили с транскрипцией. И сохранение Отключенный транскриптил.", [Languages.Russian]),
            ("Еще что-нибудь попробую специально.", [Languages.Russian])
        ];

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30).Debuggable());
        var cancellationToken = cts.Token;

        // act
        var languages = await Translator.DetectLanguages([..texts.Select(x => x.Text)], cancellationToken);

        // assert
        for (var i = 0; i < texts.Length; i++) {
            var expectedLanguages = texts[i].ExpectedLanguages;
            languages[i].Should().Contain(expectedLanguages, "for text: <<<{0}>>>", texts[i].Text);
        }
    }
}
