using ActualChat.Localization;
using ActualChat.Testing.Host;
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
        var notification = NewAggregated(shownCount: 2, moreCount: 0, authorCount: 2);

        // act
        var text = NotificationHelper.ComposeAggregatedText(notification, Russian);

        // assert
        text.Should().Be("Борис: сообщение 1\nАлиса: сообщение 0");
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

    [Theory]
    [InlineData("ru", null, "ru")]
    [InlineData(null, "ru", "ru")]
    [InlineData(null, null, "en")]
    public async Task LocationTextShouldUseTheRecipientsUILanguage(
        string? uiLanguage,
        string? detected,
        string expected)
    {
        // arrange
        var recipient = await Tester.SignInAsUniqueAlice();
        await SetUILanguages(uiLanguage, detected);
        var sender = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Localized location");
        var senderAuthor = await Tester.GetOwnAuthor(chatId).Require();
        await Tester.SignIn(recipient);
        await Tester.JoinChat(chatId, inviteId);

        // act
        await Tester.SignIn(sender);
        var entry = await Tester.CreateLocationEntry(chatId, new GeoPoint(51.5074, -0.1278));

        // assert
        var l = LanguageStringLocalizer.Get(Language.Parse(expected));
        var expectedText = EmptyEntryMarkupBuilder.LocationPin + l.EmptyEntry_SentLocation;
        var notification = await Tester.WaitForChatEntryNotification(recipient.Id, entry.Id);
        notification.LeadText.Should().Be(expectedText);
        notification.Text.Should()
            .Be(l.Notification_AuthorLine_Format(senderAuthor.Avatar.Name, expectedText));
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData(null, "en")]
    public async Task ReactionToLocationTextShouldUseTheRecipientsUILanguage(string? uiLanguage, string expected)
    {
        // arrange
        var author = await Tester.SignInAsUniqueAlice();
        await SetUILanguages(uiLanguage, null);
        var reactor = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Reacted location");
        await Tester.SignIn(author);
        await Tester.JoinChat(chatId, inviteId);
        var entry = await Tester.CreateLocationEntry(chatId, new GeoPoint(51.5074, -0.1278));

        // act
        await Tester.SignIn(reactor);
        await Tester.React(entry.Id, Emojis.Love);

        // assert
        var l = LanguageStringLocalizer.Get(Language.Parse(expected));
        var expectedText = l.Notification_Reaction_Format(Emojis.Love, l.EmptyEntry_YourLocation);
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(author.Id, CancellationToken.None);
            var reaction = info.Items.OfType<ReactionNotification>().Should().ContainSingle().Subject;
            reaction.Text.Should().Be(expectedText);
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task RingingInviteesShouldEachGetTheirOwnLanguage()
    {
        // The ring resolves its invitees together and composes once per language among them, so a
        // wrong per-invitee mapping would hand one callee the other's wording.

        // arrange
        var russianCallee = await Tester.SignInAsUniqueAlice();
        await SetUILanguages("ru", null);
        var englishCallee = await Tester.SignInAsNew("Carol");
        await SetUILanguages(null, null);
        await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Localized ring, two callees");
        var callerAuthor = await Tester.GetOwnAuthor(chatId).Require();
        var calleeAuthorIds = new List<AuthorId>();
        foreach (var callee in new[] { russianCallee, englishCallee }) {
            await Tester.SignIn(callee);
            await Tester.JoinChat(chatId, inviteId);
            calleeAuthorIds.Add((await Tester.GetOwnAuthor(chatId).Require()).Id);
        }

        // act
        await Commander.Call(new NotificationsBackend_NotifyCall(
            ConversationId.New(chatId, 1), callerAuthor.Id, calleeAuthorIds.ToApiArray(), false));

        // assert
        var english = LanguageStringLocalizer.Get(Languages.English);
        var expectations = new[] {
            (russianCallee, Russian.Call_Incoming),
            (englishCallee, english.Call_Incoming),
        };
        foreach (var (callee, expectedText) in expectations)
            await TestExt.When(async () => {
                var info = await Tester.NotificationsBackend
                    .GetUserNotificationInfo(callee.Id, CancellationToken.None);
                var ring = info.Items.OfType<CallNotification>().Should().ContainSingle().Subject;
                ring.Text.Should().Be(expectedText);
            }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task RecipientsSharingALanguageShouldNotGetEachOthersWording()
    {
        // The fan-out composes each language once and reuses it for everyone who reads it, so a
        // wrong cache key would hand the first recipient's wording to all of them.

        // arrange
        var firstRussian = await Tester.SignInAsUniqueAlice();
        await SetUILanguages("ru", null);
        var secondRussian = await Tester.SignInAsNew("Carol");
        await SetUILanguages("ru", null);
        var englishReader = await Tester.SignInAsNew("Dave");
        await SetUILanguages(null, null);
        var sender = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Mixed languages");
        foreach (var account in new[] { firstRussian, secondRussian, englishReader }) {
            await Tester.SignIn(account);
            await Tester.JoinChat(chatId, inviteId);
        }

        // act
        await Tester.SignIn(sender);
        var entry = await Tester.CreateLocationEntry(chatId, new GeoPoint(51.5074, -0.1278));

        // assert
        var english = LanguageStringLocalizer.Get(Languages.English);
        var expectations = new[] {
            (firstRussian, EmptyEntryMarkupBuilder.LocationPin + Russian.EmptyEntry_SentLocation),
            (secondRussian, EmptyEntryMarkupBuilder.LocationPin + Russian.EmptyEntry_SentLocation),
            (englishReader, EmptyEntryMarkupBuilder.LocationPin + english.EmptyEntry_SentLocation),
        };
        foreach (var (account, expectedText) in expectations) {
            var notification = await Tester.WaitForChatEntryNotification(account.Id, entry.Id);
            notification.LeadText.Should().Be(expectedText);
        }
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData(null, "en")]
    public async Task LiveLocationTextShouldSayItWasSharedLive(string? uiLanguage, string expected)
    {
        // A live share and a one-shot pin are the same entry - only the SharedLocation's Duration
        // separates them - so this is the case the push used to word as a pin.

        // arrange
        var recipient = await Tester.SignInAsUniqueAlice();
        await SetUILanguages(uiLanguage, null);
        var sender = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Localized live location");
        var senderAuthor = await Tester.GetOwnAuthor(chatId).Require();
        await Tester.SignIn(recipient);
        await Tester.JoinChat(chatId, inviteId);

        // act
        await Tester.SignIn(sender);
        var entry = await Tester.CreateLocationEntry(chatId, new GeoPoint(51.5074, -0.1278), TimeSpan.FromHours(1));

        // assert
        var l = LanguageStringLocalizer.Get(Language.Parse(expected));
        var expectedText = EmptyEntryMarkupBuilder.LocationPin + l.EmptyEntry_SentLiveLocation;
        var notification = await Tester.WaitForChatEntryNotification(recipient.Id, entry.Id);
        notification.LeadText.Should().Be(expectedText);
        notification.Text.Should()
            .Be(l.Notification_AuthorLine_Format(senderAuthor.Avatar.Name, expectedText));
    }

    [Theory]
    [InlineData("ru", null, "ru")]
    [InlineData(null, "ru", "ru")]
    [InlineData(null, null, "en")]
    public async Task AttachmentTextShouldUseTheRecipientsUILanguage(
        string? uiLanguage,
        string? detected,
        string expected)
    {
        // arrange
        var recipient = await Tester.SignInAsUniqueAlice();
        await SetUILanguages(uiLanguage, detected);
        var sender = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Localized attachments");
        var senderAuthor = await Tester.GetOwnAuthor(chatId).Require();
        await Tester.SignIn(recipient);
        await Tester.JoinChat(chatId, inviteId);

        // act
        await Tester.SignIn(sender);
        var entry = await CreateAttachmentOnlyEntry(chatId, "one.txt", "two.txt");

        // assert
        var l = LanguageStringLocalizer.Get(Language.Parse(expected));
        var expectedText = l.EmptyEntry_SentFiles(2, 2.Format());
        var notification = await Tester.WaitForChatEntryNotification(recipient.Id, entry.Id);
        notification.LeadText.Should().Be(expectedText);
        notification.Text.Should()
            .Be(l.Notification_AuthorLine_Format(senderAuthor.Avatar.Name, expectedText));
    }

    [Fact]
    public async Task AuthoredTextShouldReachEveryRecipientUntranslated()
    {
        // The counterpart of the two tests above: only text the markup layer stood in for is the
        // reader's to change - a message the author wrote is theirs in every language.

        // arrange
        var recipient = await Tester.SignInAsUniqueAlice();
        await SetUILanguages("ru", null);
        var sender = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Untranslated text");
        await Tester.SignIn(recipient);
        await Tester.JoinChat(chatId, inviteId);

        // act
        await Tester.SignIn(sender);
        var entry = await Tester.CreateTextEntry(chatId, "Hello there");

        // assert
        var notification = await Tester.WaitForChatEntryNotification(recipient.Id, entry.Id);
        notification.LeadText.Should().Be("Hello there");
    }

    // Private methods

    private async Task<Symbol> RegisterDevice(UserId userId)
    {
        var deviceId = new Symbol("test-device-" + userId.Value);
        await Commander.Call(
            new NotificationsBackend_RegisterDevice(userId, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        return deviceId;
    }

    private async Task<ChatEntry> CreateAttachmentOnlyEntry(ChatId chatId, params string[] fileNames)
    {
        var attachments = new ChatEntryAttachment[fileNames.Length];
        for (var i = 0; i < fileNames.Length; i++) {
            var mediaId = await Tester.SaveTextFile(chatId, fileNames[i], $"Content of {fileNames[i]}");
            attachments[i] = new ChatEntryAttachment { MediaId = mediaId, Index = i };
        }
        return await Tester.Commander.Call(new Chats_UpsertEntry {
            Session = Tester.Session,
            ChatId = chatId,
            LocalId = null,
            Text = "",
            Attachments = attachments,
        });
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

    private static MessageNotification NewAggregated(int shownCount, int moreCount, int authorCount = 1)
    {
        var authorIds = Enumerable.Range(0, authorCount)
            .Select(i => AuthorId.New(TestChatId, i + 1))
            .ToArray();
        var authorNames = new[] { "Алиса", "Борис", "Виктор" };
        var messages = Enumerable.Range(0, shownCount)
            .Select(i => NotificationMessage.New(
                authorIds[i % authorCount], authorNames[i % authorCount],
                $"сообщение {i}", 100 + i, Moment.EpochStart + TimeSpan.FromSeconds(i)))
            .ToApiArray();
        return MessageNotification.New(TestUserId, TestChatId, 100 + shownCount - 1, authorIds[^1]) with {
            StartEntryLid = 100,
            UnreadCount = shownCount + moreCount,
            AuthorIds = authorIds.ToApiArray(),
            RecentMessages = messages,
            LeadText = "сообщение 0",
            LeadCount = 1,
        };
    }
}
