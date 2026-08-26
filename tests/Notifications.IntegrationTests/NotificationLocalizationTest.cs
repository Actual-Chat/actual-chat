using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationLocalizationTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly UserId TestUserId = UserId.New();
    // Russian throughout: three plural branches make a wrong form obvious.
    private static readonly IStringLocalizer Russian = LanguageStringLocalizer.Get(Languages.Russian);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();

    [Theory]
    [InlineData(1, "+1 предыдущее сообщение")]
    [InlineData(2, "+2 предыдущих сообщения")]
    [InlineData(5, "+5 предыдущих сообщений")]
    public void AggregatedTextShouldUseThePluralFormTheCountNeeds(int moreCount, string expectedTail)
    {
        // arrange
        var notification = NewAggregated(shownCount: 2, moreCount);

        // act
        var text = NotificationHelper.ComposeAggregatedText(notification, Russian);

        // assert
        text.Should().EndWith(expectedTail, "the plural key must pick Russian's form for {0}", moreCount);
    }

    [Fact]
    public void AggregatedTextShouldPrefixEveryLineWithItsAuthor()
    {
        // arrange
        var notification = NewAggregated(shownCount: 2, moreCount: 0);

        // act
        var text = NotificationHelper.ComposeAggregatedText(notification, Russian);

        // assert
        text.Should().Be("Алиса: сообщение 1\nАлиса: сообщение 0");
    }

    [Fact]
    public void VoiceChatStartedTextShouldBeGenericWithoutNames()
        => NotificationHelper.GetVoiceChatStartedText([], Russian)
            .Should().Be(Russian.Notification_VoiceChatStarted);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(Constants.Notification.MaxSummaryAuthors)]
    public void VoiceChatStartedTextShouldNameEveryShownAuthor(int nameCount)
    {
        // arrange
        var names = NewNames(nameCount);

        // act
        var text = NotificationHelper.GetVoiceChatStartedText(names, Russian);

        // assert
        text.Should().NotBe(Russian.Notification_VoiceChatStarted);
        foreach (var name in names)
            text.Should().Contain(name, "every shown author must survive into the banner");
    }

    [Fact]
    public void VoiceChatStartedTextShouldCountAuthorsBeyondTheWindow()
    {
        // arrange
        var names = NewNames(Constants.Notification.MaxSummaryAuthors + 3);

        // act
        var text = NotificationHelper.GetVoiceChatStartedText(names, Russian);

        // assert
        text.Should().Be("Участник0, Участник1, Участник2 и ещё 3 начинают голосовой чат");
        text.Should().NotContain(names[^1], "authors past the window are not named");
    }

    [Theory]
    // The subject's number reaches the verb - English hides this.
    [InlineData(1, "Участник0 начинает голосовой чат")]
    [InlineData(2, "Участник0 и Участник1 начинают голосовой чат")]
    public void VoiceChatStartedTextShouldAgreeWithTheAuthorCount(int nameCount, string expected)
        => NotificationHelper.GetVoiceChatStartedText(NewNames(nameCount), Russian)
            .Should().Be(expected);

    [Theory]
    [InlineData("ru", null, "ru")]
    [InlineData(null, "ru", "ru")]
    [InlineData("ru", "de", "ru")]
    [InlineData(null, null, "en")]
    public async Task CallTextShouldUseTheRecipientsUILanguage(string? uiLanguage, string? detected, string expected)
    {
        // arrange
        var callee = await Tester.SignInAsUniqueAlice();
        await SetUILanguages(uiLanguage, detected);
        await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Localized ring");
        var callerAuthor = await Tester.GetOwnAuthor(chatId).Require();
        await Tester.SignIn(callee);
        await Tester.JoinChat(chatId, inviteId);
        var calleeAuthor = await Tester.GetOwnAuthor(chatId).Require();
        var deviceId = await RegisterDevice(callee.Id);
        Sink.Clear();

        // act
        await Commander.Call(new NotificationsBackend_NotifyCall(
            ConversationId.New(chatId, 1), callerAuthor.Id, new[] { calleeAuthor.Id }.ToApiArray(), false));

        // assert
        var expectedText = LanguageStringLocalizer.Get(Language.Parse(expected)).Call_Incoming;
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(callee.Id, CancellationToken.None);
            var ring = info.Items.OfType<CallNotification>().Should().ContainSingle().Subject;
            ring.Text.Should().Be(expectedText);
        }, TimeSpan.FromSeconds(15));

        // The stored text is what the in-app list renders; this is the same value leaving for
        // the device, which is what every platform's banner shows.
        await TestExt.When(() => {
            Sink.Messages
                .Where(m => !m.IsDismissal && m.DeviceIds.Contains(deviceId))
                .Select(m => m.Notification!.Text)
                .Should().Contain(expectedText);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    // Private methods

    private async Task<Symbol> RegisterDevice(UserId userId)
    {
        var deviceId = new Symbol("test-device-" + userId.Value);
        await Commander.Call(
            new NotificationsBackend_RegisterDevice(userId, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        return deviceId;
    }

    private async Task SetUILanguages(string? uiLanguage, string? detected)
        => await Tester.AppServices.UserSettingsUI(Tester.Session)
            .UserLanguageSettings()
            .Update(x => x with {
                UILanguage = uiLanguage is null ? null : Language.Parse(uiLanguage),
                DetectedUILanguage = detected is null ? null : Language.Parse(detected),
            }, CancellationToken.None);

    private static string[] NewNames(int count)
        => Enumerable.Range(0, count).Select(i => $"Участник{i}").ToArray();

    private static MessageNotification NewAggregated(int shownCount, int moreCount)
    {
        var authorId = AuthorId.New(TestChatId, 1);
        var messages = Enumerable.Range(0, shownCount)
            .Select(i => NotificationMessage.New(
                authorId, "Алиса", $"сообщение {i}", 100 + i, Moment.EpochStart + TimeSpan.FromSeconds(i)))
            .ToApiArray();
        return MessageNotification.New(TestUserId, TestChatId, 100 + shownCount - 1, authorId) with {
            StartEntryLid = 100,
            UnreadCount = shownCount + moreCount,
            AuthorIds = new[] { authorId }.ToApiArray(),
            RecentMessages = messages,
            LeadText = "сообщение 0",
            LeadCount = 1,
        };
    }
}
