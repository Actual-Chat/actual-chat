using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(TranslationCollection))]
[Trait("Category", "Nightly")]
public class LanguageDetectorTest(TranslationCollection.AppHostFixture fixture, ITestOutputHelper @out)
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
    private LanguageDetector Sut => field ??= Tester.AppServices.GetRequiredService<LanguageDetector>();



    [Theory]
    [InlineData(ComplexText, "en-US")]
    [InlineData("Hello! Привет! Bonjour!", "en-US", "ru-RU", "fr-FR")]
    [InlineData("Hello! Привет! Bonjour! Привет!", "en-US", "ru-RU", "fr-FR")]
    [InlineData("hello", "en-US")]
    [InlineData("привет", "ru-RU")]
    [InlineData("```")]
    [InlineData("````123```")]
    [InlineData("123")]
    [InlineData("0xDEADBEEF")]
    [InlineData("Hello! @#$%^&*()_+", "en-US")]
    [InlineData("1998: Most amazing amazing chat absolutely amazing chat actual actual most most terrific terrific chat terrific ever absolutely terrific terrific level 100500", "en-US")]
    public async Task ShouldDetectLanguages(string text, params string[] sExpectedLanguages)
    {
        // arrange
        var expectedLanguages = sExpectedLanguages.Select(Language.Parse).ToList();
        var timeout = TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(10);
        var cts = new CancellationTokenSource(timeout.Debuggable());
        var cancellationToken = cts.Token;

        // act
        var languages = await Sut.DetectLanguages(text, cancellationToken);

        // assert
        languages.Should().BeEquivalentTo(expectedLanguages, "for text: <<<{0}>>>", text);
    }

    [Theory]
    [InlineData("This is a long English sentence with many words that clearly indicate the primary language of this message, but it contains one word in Russian: привет", "en-US")]
    [InlineData("Это длинное русское предложение с множеством слов, которое явно указывает на основной язык этого сообщения, но оно содержит одно слово на английском: hello", "ru-RU")]
    public async Task ShouldDetectOnlyPrimaryLanguageWhenOneWordIsInAnotherLanguage(string text, string expectedLanguage)
    {
        // arrange
        var expectedLanguages = new[] { Language.Parse(expectedLanguage) };
        var timeout = TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(10);
        var cts = new CancellationTokenSource(timeout.Debuggable());
        var cancellationToken = cts.Token;

        // act
        var languages = await Sut.DetectLanguages(text, cancellationToken);

        // assert
        languages.Should().BeEquivalentTo(expectedLanguages, "for text: <<<{0}>>>", text);
    }

    [Theory]
    [InlineData("hello", "en-US")]
    [InlineData("До скорых.", "ru-RU")]
    [InlineData("Попытка", "ru-RU")]
    [InlineData("Развод.", "ru-RU")]
    [InlineData("Probably that from there. Probably.", "en-US")]
    [InlineData("I don't mind.", "en-US")]
    [InlineData("I'm a Google guy right now.", "en-US")]
    [InlineData("Нет, мы не били.", "ru-RU")]
    [InlineData("bonjour", "fr-FR")]
    [InlineData("или не надо", "ru-RU")]
    [InlineData("Давай попробуем", "ru-RU")]
    [InlineData("Раз два три, 4 раз два, три, 4, 5", "ru-RU")]
    [InlineData("Развод Мы тестируем где мы тестируем.", "ru-RU")]
    [InlineData("Joseph.", "en-US")]
    [InlineData("Merhaba", "tr-TR")]
    [InlineData("Если что-нибудь попробую.", "ru-RU")]
    [InlineData("Я хочу что-нибудь попробовать катать.", "ru-RU")]
    [InlineData("Вязи хочу наговорить большой текст, чтобы у нас было понятно, какой текст транскрипируется, и Какой Что хранит VLOST Transcript?", "ru-RU")]
    [InlineData("Guten Tag.", "de-DE")]
    [InlineData("Разворачиваем.", "ru-RU")]
    [InlineData("Давай.", "ru-RU")]
    [InlineData("Нет не серьёзно.", "ru-RU")]
    [InlineData("А так попробуй распо. Распознать", "ru-RU")]
    [InlineData("Попробуем", "ru-RU")]
    [InlineData("Я помнишь, как саголикон и ты тоже их майко.", "ru-RU")]
    [InlineData("parle français.", "fr-FR")]
    [InlineData("Hi.", "en-US")]
    [InlineData("The meta balance Good.", "en-US")]
    [InlineData("Но я моё!", "ru-RU")]
    [InlineData("Серьезно?", "ru-RU")]
    [InlineData("давай переводи", "ru-RU")]
    [InlineData("Что за хренотал?", "ru-RU")]
    [InlineData("Привет. Я должен осознать язык, точнее взять из конструкции то есть.", "ru-RU")]
    [InlineData("Я говорю", "ru-RU")]
    [InlineData("1 I don't mind this.", "en-US")]
    [InlineData("Я снова попробую наговорить текст. Я сейчас буду наговаривать текст.", "ru-RU")]
    [InlineData("Good and tag. Our feeder is in.", "en-US")]
    [InlineData("Мы тестируем дипграмму.", "ru-RU")]
    [InlineData("Пробуем записывать.", "ru-RU")]
    [InlineData("Аминь, айпидок! А.", "ru-RU")]
    [InlineData("Раз, два, три.", "ru-RU")]
    [InlineData("Так, ну как это давай попробуем. Потерять язык, который мы получили с транскрипцией. И сохранение Отключенный транскриптил.", "ru-RU")]
    [InlineData("Еще что-нибудь попробую специально.", "ru-RU")]
    public async Task ShouldDetectLanguagesForManyEntries(string text, string expectedLanguage)
    {
        // arrange
        var expectedLanguages = new[] { Language.Parse(expectedLanguage) };
        var timeout = TimeSpan.FromSeconds(30);
        var cts = new CancellationTokenSource(timeout.Debuggable());
        var cancellationToken = cts.Token;

        // act
        var languages = await Sut.DetectLanguages(text, cancellationToken);

        // assert
        languages.Should().Contain(expectedLanguages, "for text: <<<{0}>>>", text);
    }
}
