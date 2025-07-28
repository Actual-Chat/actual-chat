using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Transcription;
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
    private TranslationUI TranslationUI => field ??= BobTester.ScopedAppServices.GetRequiredService<TranslationUI>();
    [field: AllowNull, MaybeNull]
    private LanguageUI LanguageUI => field ??= BobTester.ScopedAppServices.GetRequiredService<LanguageUI>();
    [field: AllowNull, MaybeNull]
    private ChatUI ChatUI => field ??= BobTester.ScopedAppServices.GetRequiredService<ChatUI>();

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
        await WhenMustTranslate(frenchEntry.TextEntry, true);
        await WhenMustTranslate(englishEntry.TextEntry, false);

        // act
        englishEntry = await AliceTester.FinalizeStreamingEntry(englishEntry, "Hello!", cancellationToken);
        frenchEntry = await AliceTester.FinalizeStreamingEntry(frenchEntry, "Bonjour!", cancellationToken);

        // assert
        await WhenMustTranslate(frenchEntry.TextEntry, true);
        await WhenMustTranslate(englishEntry.TextEntry, true);
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
        await WhenMustTranslate(streamingEntry.TextEntry, true);
        await AssertTranslation(streamingEntry.TextEntry, "Hello!");
    }

    [Fact]
    public async Task ShouldStreamHistoricalTranslationForLongText()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var chatId = await CreateChat(cancellationToken);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English, cancellationToken);
        var text = """
                   В своих произведениях Айзек Азимов иногда привносит в Три Закона различные модификации и опровергает их, как бы испытывая Законы «на прочность» в разных обстоятельствах.

                   Нулевой Закон
                   Айзек Азимов однажды добавил Нулевой Закон, сделав его более приоритетным, чем три основных. Этот закон утверждал, что робот должен действовать в интересах всего человечества, а не только отдельного человека. Вот как выражает его робот Дэниел Оливо в романе «Основание и Земля»:
                   """;
        var expected = """
                       In his works, Isaac Asimov sometimes introduces various modifications to the Three Laws and refutes them, as if testing the Laws "for strength" under different circumstances.

                       The Zeroth Law
                       Isaac Asimov once added the Zeroth Law, making it a higher priority than the three main ones. This law stated that a robot must act in the interests of all humanity, not just an individual person. Here is how robot Daneel Olivaw expresses it in the novel "Foundation and Earth":
                       """;
        var entries = await CreateVisibleEntries(chatId, text);
        var entry = entries[0];

        // act
        var translation = await AssertTranslation(entry, "");

        // assert
        translation.IsStreaming.Should().Be(true);

        // act
        var diffs = await TranslationUI.GetTranscript(entries[0], cancellationToken).ToListAsync(cancellationToken);

        // assert
        diffs.Should().HaveCountGreaterThan(10);
        var transcript = diffs.ToTranscripts().Last();
        transcript.Text.Should().BeSimilarTo(expected, 0.6);

        // act
        await AssertTranslation(entry, expected);
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

    private Task WhenMustTranslate(ChatEntry entry, bool expected)
        => ComputedTest.When(async ct => {
            var mustTranslate = await TranslationUI.MustTranslate(entry, entry.IsStreaming, ct);
            mustTranslate.Should().Be(expected);
        }, TimeSpan.FromSeconds(10).Debuggable());

    private Task<Translation> AssertTranslation(ChatEntry entry, string expected)
        => ComputedTest.When(async ct => {
            var translation = await TranslationUI.Get(entry.Id.ToTextEntryId(), ct).Require();
            if (expected.IsNullOrEmpty())
                translation.Content.Should().Be(expected);
            else
                translation.Content.Should().BeSimilarTo(expected, 0.7);
            translation.SourceContentHash.Should().Be(entry.ContentHash);
            return translation;
        }, TimeSpan.FromSeconds(10).Debuggable());
}
