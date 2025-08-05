using System.Diagnostics.CodeAnalysis;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Transcription;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(TranslationUICollection))]
public class TranslationUITest(TranslationAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TranslationAppHostFixture>(fixture, @out)
{
    [field: AllowNull, MaybeNull]
    private BlazorTester BobTester => field ??= AppHost.NewBlazorTester(Out);
    [field: AllowNull, MaybeNull]
    private BlazorTester AliceTester => field ??= AppHost.NewBlazorTester(Out);
    [field: AllowNull, MaybeNull]
    private AppUIHub Hub => field ??= BobTester.ScopedAppServices.AppUIHub();
    private IStreamClient StreamClient => Hub.StreamClient;
    private TranslationUI TranslationUI => Hub.TranslationUI;
    private TranscriptUI TranscriptUI => Hub.TranscriptUI;
    private LanguageUI LanguageUI => Hub.LanguageUI;
    private ChatUI ChatUI => Hub.ChatUI;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await AliceTester.SignInAsUniqueAlice();
        await BobTester.SignInAsUniqueBobAdmin();
        await LanguageUI.WhenReady.WaitAsync(TimeSpan.FromSeconds(5).Debuggable());
        var appSettings = await BobTester.AccountSettings.GetUserAppSettings(CancellationToken.None);
        await BobTester.AccountSettings.SetUserAppSettings(appSettings with {
            IsIncompleteUIEnabled = true,
            AreExperimentalFeaturesEnabled = true,
        }, CancellationToken.None);
    }

    protected override async Task DisposeAsync()
    {
        await BobTester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData]
    [InlineData("en")]
    [InlineData("en", "ru")]
    public async Task SubHeaderShouldBeVisibleByDefaultWhenAnyForeignEntry(string sPrimary = "", string sSecondary = "")
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var chatId = await CreateChat(cancellationToken);
        var primary = Language.ParseNullable(sPrimary);
        var secondary = Language.ParseNullable(sSecondary);
        if (primary is not null || secondary is not null)
            await LanguageUI.UpdateSettings(x => x with { Primary = primary!, Secondary = secondary });

        // act
        await CreateVisibleEntries(chatId, "Bonjour");

        // assert
        await AssertIsSubHeaderVisible(chatId, true);
    }

    [Fact]
    public async Task ShouldNotBeVisibleByDefaultWhenNoForeignEntry()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var chatId = await CreateChat(cancellationToken);

        // act
        await CreateVisibleEntries(chatId,"Does not need translation");

        // assert
        await AssertIsSubHeaderVisible(chatId, false);
    }

    [Fact]
    public async Task ShouldNotBeVisibleIfForeignEntryIsNotVisible()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var chatId = await CreateChat(cancellationToken);

        // act
        var entries = await CreateVisibleEntries(chatId, "Does not need translation", "Bonjour");
        var englishEntry = entries[0];
        SetVisibleItems(englishEntry);

        // assert
        await AssertIsSubHeaderVisible(chatId, false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsOnShouldNotAffectSubheaderVisibility(bool initialIsOn)
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var chatId = await CreateChat(cancellationToken);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English, cancellationToken);
        await TranslationUI.SetIsSubHeaderVisible(chatId, true, cancellationToken);

        // act
        await TranslationUI.SetIsOn(chatId, initialIsOn, cancellationToken);

        // assert
        await AssertIsSubHeaderVisible(chatId, true);

        // act
        await TranslationUI.SetIsOn(chatId, !initialIsOn, cancellationToken);

        // assert
        await AssertIsSubHeaderVisible(chatId, true);
    }

    [Fact]
    public async Task MustTranslateShouldConsiderIsOn()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var chatId = await CreateChat(cancellationToken);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English, cancellationToken);
        await TranslationUI.SetIsSubHeaderVisible(chatId, true, cancellationToken);
        await LanguageUI.UpdateSettings(settings => settings with {
            Primary = Languages.English,
            Secondary = Languages.French,
        });
        await TranslationUI.SetTargetLanguage(chatId, Languages.French, cancellationToken);

        // act
        await TranslationUI.SetIsOn(chatId, true, cancellationToken);
        var entries = await CreateVisibleEntries(chatId, "Hello!", "Bonjour!");
        var mustTranslateEnglishEntry = await TranslationUI.MustTranslate(entries[0], false, cancellationToken);
        var mustTranslateFrenchEntry = await TranslationUI.MustTranslate(entries[1], false, cancellationToken);

        // assert
        mustTranslateEnglishEntry.Should().BeTrue();
        mustTranslateFrenchEntry.Should().BeTrue();
    }

    [Fact]
    public async Task MustTranslateShouldConsiderIsForeignEntryForStreaming()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var chatId = await CreateChat(cancellationToken);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English, cancellationToken);

        // act
        await TranslationUI.SetIsOn(chatId, true, cancellationToken);
        var englishEntry = await AliceTester.CreateStreamingEntry(chatId, Languages.English, cancellationToken);
        var frenchEntry = await AliceTester.CreateStreamingEntry(chatId, Languages.French, cancellationToken);

        // assert
        await AssertMustTranslate(frenchEntry.TextEntry, true);
        await AssertMustTranslate(englishEntry.TextEntry, false);

        // act
        englishEntry = await AliceTester.FinalizeStreamingEntry(englishEntry, "Hello!", cancellationToken);
        frenchEntry = await AliceTester.FinalizeStreamingEntry(frenchEntry, "Bonjour!", cancellationToken);

        // assert
        await AssertMustTranslate(frenchEntry.TextEntry, true);
        await AssertMustTranslate(englishEntry.TextEntry, false);
    }

    [Fact]
    public async Task ShouldTranslateHistoricalTranscribedMessages()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var chatId = await CreateChat(cancellationToken);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English, cancellationToken);

        // act
        await TranslationUI.SetIsOn(chatId, true, cancellationToken);
        var streamingEntry = await AliceTester.CreateStreamingEntry(chatId, Languages.French, cancellationToken);
        streamingEntry = await AliceTester.FinalizeStreamingEntry(streamingEntry, "Bonjour!", cancellationToken);

        // assert
        await AssertMustTranslate(streamingEntry.TextEntry, true);
        await AssertTranslation(streamingEntry.TextEntry, "Hello!");
    }

    [Fact]
    public async Task ShouldStreamHistoricalTranslationForLongText()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60).Debuggable());
        var cancellationToken = cts.Token;
        await BobTester.AccountSettings.UpdateUserAppSettings(x => x with { IsIncompleteUIEnabled = true }, cancellationToken);
        var chatId = await CreateChat(cancellationToken);
        await TranslationUI.SetIsOn(chatId, true, cancellationToken);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English, cancellationToken);
        var text = """
                   В своих произведениях Айзек Азимов иногда привносит в Три Закона различные модификации и опровергает их, как бы испытывая Законы «на прочность» в разных обстоятельствах.

                   Нулевой Закон
                   Айзек Азимов однажды добавил Нулевой Закон, сделав его более приоритетным, чем три основных. Этот закон утверждал, что робот должен действовать в интересах всего человечества, а не только отдельного человека. Вот как выражает его робот Дэниел Оливо в романе «Основание и Земля»:



                   0. Робот не может нанести вред человечеству или своим бездействием допустить, чтобы человечеству был нанесён вред.Именно он был первым, кто дал этому закону номер — это произошло в романе «Роботы и империя», правда, само понятие ещё раньше сформулировала Сьюзен Келвин — в новелле «Разрешимое противоречие».
                   Первыми роботами, которые стали подчиняться Нулевому Закону, причём по собственной воле, — были Жискар Ривентлов и Дэниел Оливо. Это описано в одной из финальных сцен романа «Роботы и Империя», когда роботу необходимо было проигнорировать приказ одного человека ради прогресса всего человечества. Нулевой Закон не был заложен в позитронный мозг Жискара и Дэниэла — они пытались прийти к нему через чистое понимание Жискара и детективный опыт Дэниэла, через более тонкое, чем у всех остальных роботов, осознание понятия вреда. Однако Жискар не был уверен, насколько это было полезно для человечества, что негативно сказалось на его мозге. Будучи телепатом, Жискар перед выходом из строя передал Дэниелу свои телепатические способности. Только через много тысяч лет Дэниэл Оливо смог полностью приспособиться к подчинению Нулевому Закону.

                   Французский переводчик Жак Брекар невольно сформулировал Нулевой Закон раньше, чем Азимов описал его явно. Ближе к завершению романа «Стальные пещеры» Элайдж Бейли замечает, что Первый Закон запрещает роботу наносить человеку вред, если только не будет уверенности, что это будет полезно для него в будущем. Во французском переводе («Les Cavernes d’acier», 1956 год) мысли Бейли переданы несколько иначе[13]:

                   Робот не может причинить вреда человеку, если только он не докажет, что в конечном итоге это будет полезно для всего человечества.

                   Оригинальный текст (фр.)
                   Примечательно, что логическое развитие Первого Закона до Нулевого предложили создатели фильма «Я, робот» 2004 года. Когда суперкомпьютер В. И. К. И. принимает решение ограничить свободу жителей планеты, чтобы они ненароком не нанесли вреда друг другу и своему будущему, она выполняет не Первый Закон, а именно Нулевой. Ещё интереснее то, что таким образом в фильме показывается противоречие Нулевого Закона Первому, его неэтичность. Действительно, когда речь идёт о благе человечества, система не может рассматривать людей по отдельности, а значит, ей ничто не мешает нарушить права и свободу любого или даже каждого человека (фактически, в «Разрешимом противоречии» позитронные суперкомпьютеры уже вели себя аналогично, с той разницей, что вред индивидуумам они старались снизить до предела). Во время войн, а нередко и в мирной жизни, люди наносят вред себе, окружающим, своей культуре. Поэтому на основании Нулевого Закона совершенно логично держать людей под постоянной опекой, не выполняя приказы столь неразумных существ.

                   Модификация Первого Закона
                   В автобиографических записях Азимова говорится, что вторая часть Первого Закона появилась из-за сатирической поэмы Артура Хью Клафа «Последний декалог», где есть такая строка: «Не убей, но и не слишком старайся сохранить другому жизнь».

                   В рассказе «Как потерялся робот» несколько роботов из серии НС (Нестор) были выпущены только с «половиной» Первого Закона, которая звучит так:

                   Робот не может причинить вреда человеку.
                   Это было сделано из практических соображений: роботы работали вместе с людьми, которые подвергались воздействию небольших безопасных доз радиации. Соблюдая наиболее приоритетный Первый Закон, роботы всё время бросались «спасать» людей. Так как позитронный мозг крайне уязвим для гамма-лучей, роботы постоянно выходили из строя. Усечение Первого Закона решало эту проблему, но при этом создавало бо́льшую: робот мог не предпринять никаких действий по спасению находящегося в бедствии человека, так как такой поступок не противоречит усечённому Первому Закону.
                   """;
        var expected = """
                       In his works, Isaac Asimov sometimes introduces various modifications to the Three Laws and refutes them, as if testing the Laws' "strength" under different circumstances.

                       The Zeroth Law
                       Isaac Asimov once added the Zeroth Law, making it even more important than the three main ones. This law stated that a robot must act in the interests of all humanity, not just an individual person. Here’s how robot Daneel Olivaw expresses it in the novel “Foundation and Earth”:

                       0. A robot may not harm humanity or, through inaction, allow humanity to come to harm.

                       He was actually the first to assign this law a number—in the novel “Robots and Empire,” although Susan Calvin had already formulated this concept earlier—in the short story “The Evitable Conflict.” The first robots who began obeying the Zeroth Law of their own free will were Giskard Reventlov and Daneel Olivaw. This is described in one of the final scenes of “Robots and Empire,” when a robot needs to ignore an order from one person for the sake of progress for all humanity. The Zeroth Law was not programmed into Giskard’s or Daneel’s positronic brains—they tried to arrive at it through Giskard’s pure understanding and Daneel’s detective experience, with a subtler awareness of harm than other robots possessed. However, Giskard wasn’t sure how beneficial this was for humanity overall, which negatively affected his brain. Being telepathic, before breaking down completely Giskard passed on his telepathic abilities to Daneel. Only many thousands of years later did Daneel Olivaw fully adapt himself to obeying the Zeroth Law.

                       French translator Jacques Brécard inadvertently formulated something like a Zeroth Law before Asimov explicitly described it. Nearing completion of “The Caves of Steel,” Elijah Baley notes that while First Law forbids robots from harming humans unless they are certain such action would benefit them in future; but in French translation (“Les Cavernes d’acier”, 1956), Baley's thoughts are rendered slightly differently[13]:

                       A robot cannot harm a human being unless he can prove that ultimately it will be beneficial for all mankind.

                       Original text (fr.)
                       It is noteworthy that logical development from First Law toward a Zeroth was suggested by creators of 2004 film "I, Robot." When supercomputer V.I.K.I decides to restrict planetary inhabitants’ freedom so they don’t accidentally harm each other or their future—it is following not First but precisely Zeroeth Law. Even more interestingly: thus film shows contradiction between Zeroeth and First Laws—its lack of ethics! Indeed: when considering benefit for mankind as whole—the system cannot consider individuals separately; nothing prevents violating rights/freedom any—or even every—person (in fact positronic supercomputers behaved similarly already in "The Evitable Conflict," except they tried minimizing individual harm). During wars—and often even peacetime—people cause damage themselves/others/their culture; therefore based on Zeroeth logically people should be kept under constant supervision without following orders from such irrational beings.

                       Modification Of The First Law
                       Asimov writes autobiographically that second part Of The First Law appeared because Arthur Hugh Clough's satirical poem "The Latest Decalogue" contains line: "Thou shalt not kill; but needst not strive officiously to keep alive."

                       In story "Little Lost Robot," several NS-series (Nestor) robots were released with only half Of The First Law:

                       A robot may not injure a human being.
                       This was done for practical reasons: these robots worked alongside people exposed safely small doses radiation; strictly observing highest-priority First Law meant constantly rushing off trying save humans—which led positronic brains (highly vulnerable gamma rays) frequently failing/breaking down! Truncating The First solved problem—but created bigger one: now robot might take no action at all saving someone endangered since such behavior does NOT contradict truncated version Of The First!
                       """;
        var entries = await CreateVisibleEntries(chatId, text);
        var entry = entries[0];

        // act, assert
        await AssertTranslation(entry, "");
        var streamingState = await AssertIsStreaming(entry, true).Require();

        // act
        var diffs = await StreamClient.GetTranscript(streamingState.StreamId.Value, cancellationToken).ToListAsync(cancellationToken);

        // assert
        diffs.Should().HaveCountGreaterThan(10);
        var transcript = diffs.ToTranscripts().Last();
        transcript.Text.Should().BeSimilarTo(expected, 0.5);

        // act
        await AssertTranslation(entry, expected, 0.5);
    }


    private async Task<ChatId> CreateChat(CancellationToken cancellationToken)
    {
        var (chatId, _) = await BobTester.CreateChat(true, cancellationToken: cancellationToken);
        await AliceTester.JoinChat(chatId, Symbol.Empty, false);
        return chatId;
    }

    private async Task<ChatEntry[]> CreateVisibleEntries(ChatId chatId, params string[] texts)
    {
        await AliceTester.JoinChat(chatId, Symbol.Empty, false);
        var entries = await texts.Select(x => AliceTester.CreateTextEntry(chatId, x)).Collect();
        SetVisibleItems(entries);
        return entries;
    }

    private void SetVisibleItems(params IReadOnlyCollection<ChatEntry> entries)
    {
        var chatId = entries.Select(x => x.ChatId).Distinct().Single();
        var lids = entries.Select(x => x.LocalId).ToHashSet();
        ChatUI.ItemVisibility.Value = new ChatViewItemVisibility(chatId, lids, true);
    }

    private Task AssertIsSubHeaderVisible(ChatId chatId, bool expected)
        => ComputedTest.When(async ct => {
            var isVisible = await TranslationUI.IsSubHeaderVisible(chatId, ct);
            isVisible.Should().Be(expected);
        });

    private Task AssertMustTranslate(ChatEntry entry, bool expected)
        => ComputedTest.When(async ct => {
            var mustTranslate = await TranslationUI.MustTranslate(entry, entry.IsStreaming, ct);
            mustTranslate.Should().Be(expected);
        }, TimeSpan.FromSeconds(10).Debuggable());

    private Task<TranscriptUI.StreamingState?> AssertIsStreaming(ChatEntry entry, bool expected)
        => ComputedTest.When(async ct => {
            var streamingState = await TranscriptUI.GetStreamingState(entry.Id.ToTextEntryId(), ct);
            var isStreaming = streamingState?.IsTranslation;
            isStreaming.Should().Be(expected);
            return streamingState;
        }, TimeSpan.FromSeconds(10).Debuggable());

    private Task<Translation> AssertTranslation(ChatEntry entry, string expected, double similarity = 0.7)
        => ComputedTest.When(async ct => {
            var translation = await TranslationUI.Get(entry.Id.ToTextEntryId(), ct).Require();
            if (expected.IsNullOrEmpty())
                translation.Content.Should().Be(expected);
            else
                translation.Content.Should().BeSimilarTo(expected, similarity);
            translation.SourceContentHash.Should().Be(entry.ContentHash);
            return translation;
        }, TimeSpan.FromSeconds(10).Debuggable());
}
