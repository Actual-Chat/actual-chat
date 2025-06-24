using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;
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
        await LanguageUI.WhenReady.WaitAsync(TimeSpan.FromSeconds(5));
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
        var primary = Language.ParseNullable(sPrimary);
        var secondary = Language.ParseNullable(sSecondary);
        if (primary is not null || secondary is not null)
            await LanguageUI.UpdateSettings(x => x with { Primary = primary!, Secondary = secondary });
        var (chatId, _) = await BobTester.CreateChat(true);

        // act
        await CreateVisibleEntries(chatId, "Bonjour");

        // assert
        await AssertIsSubHeaderVisible(chatId, true);
    }

    [Fact]
    public async Task ShouldNotBeVisibleByDefaultWhenNoForeignEntry()
    {
        // arrange
        var (chatId, _) = await BobTester.CreateChat(true);

        // act
        await CreateVisibleEntries(chatId,"Does not need translation");

        // assert
        await AssertIsSubHeaderVisible(chatId, false);
    }

    [Fact]
    public async Task ShouldNotBeVisibleIfForeignEntryIsNotVisible()
    {
        // arrange
        var (chatId, _) = await BobTester.CreateChat(true);

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
        var (chatId, _) = await BobTester.CreateChat(true);
        await TranslationUI.SetTargetLanguage(chatId, Languages.English);
        await TranslationUI.SetIsSubHeaderVisible(chatId, true);

        // act
        await TranslationUI.SetIsOn(chatId, initialIsOn);

        // assert
        await AssertIsSubHeaderVisible(chatId, true);

        // act
        await TranslationUI.SetIsOn(chatId, !initialIsOn);

        // assert
        await AssertIsSubHeaderVisible(chatId, true);
    }

    [Fact]
    public async Task MustTranslateShouldConsiderIsOn()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15).Debuggable());
        var cancellationToken = cts.Token;
        var (chatId, _) = await BobTester.CreateChat(true);
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
}
