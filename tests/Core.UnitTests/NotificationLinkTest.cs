using ActualChat.Notifications;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.Core.UnitTests;

public class NotificationLinkTest
{
    private static readonly UserId OwnUserId = UserId.New();
    private static readonly ChatId GroupChatId = ChatId.Parse("s-P7oXNDTeHL-752w3sfrad");
    private static readonly ChatEntryId EntryId = ChatEntryId.New(GroupChatId, 1234);

    [Fact]
    public void ShortIdShouldNotCarryTheUserId()
    {
        // arrange
        var id = MentionNotification.New(OwnUserId, EntryId).Id;

        // act
        var shortId = id.ToShort();

        // assert
        shortId.Should().Be(id.Value[(OwnUserId.Value.Length + 1)..]);
        shortId.Should().StartWith($"{(int)NotificationKind.Mention}:");
        NotificationId.TryParseShort(OwnUserId, shortId).Should().Be(id);
        NotificationId.ParseShort(OwnUserId, shortId).Should().Be(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("x:y")]
    [InlineData("99:y")]
    [InlineData("0:y")]
    public void ShortIdShouldRejectGarbage(string shortId)
    {
        // act & assert
        NotificationId.TryParseShort(OwnUserId, shortId).Should().BeNull();
        NotificationId.TryParseShort(OwnUserId, null).Should().BeNull();
        var parse = () => NotificationId.ParseShort(OwnUserId, shortId);
        parse.Should().Throw<Exception>();
    }

    [Fact]
    public void ShortIdShouldParseFromBothFormsTheRouterMayHandOver()
    {
        // arrange
        var id = MentionNotification.New(OwnUserId, EntryId).Id;
        var encoded = id.ToShort().UrlEncode();
        var decoded = id.ToShort();

        // act
        var fromEncoded = Links.TryParseNotification(OwnUserId, encoded);
        var fromDecoded = Links.TryParseNotification(OwnUserId, decoded);

        // assert
        encoded.Should().Contain("%3A", "a ':' is written escaped by the encoder the links use");
        fromEncoded.Should().Be(id);
        fromDecoded.Should().Be(id);
    }

    [Fact]
    public void LinkOfAChatNotificationShouldBeTheChatLinkWithTheNotificationAndTheUI()
    {
        // arrange
        var id = MentionNotification.New(OwnUserId, EntryId).Id;

        // act
        var link = Links.Notification(id);
        var isChat = link.IsChat(out var chatId, out long entryLid);

        // assert
        link.Value.Should().StartWith($"/chat/{GroupChatId.Value}?n=1234");
        link.Value.Should().NotContain(OwnUserId.Value, "the user is always the current one");
        isChat.Should().BeTrue();
        chatId.Should().Be(GroupChatId);
        entryLid.Should().Be(1234);
        var query = GetQuery(link);
        Links.TryParseNotification(OwnUserId, query[Links.NotificationIdQueryParameterName]).Should().Be(id);
        NotificationUIModeExt.Parse(query[Links.NotificationUIQueryParameterName])
            .Should().Be(NotificationUIMode.Notifications);
    }

    [Fact]
    public void LinkOfAChatLevelNotificationShouldCarryTheEntryItIsGiven()
    {
        // arrange
        var id = MessageNotification.New(OwnUserId, GroupChatId).Id;

        // act
        var link = Links.Notification(id, 77);
        var isChat = link.IsChat(out var chatId, out long entryLid);

        // assert
        isChat.Should().BeTrue();
        chatId.Should().Be(GroupChatId);
        entryLid.Should().Be(77);
        GetQuery(link)[Links.NotificationIdQueryParameterName].Should().Be(id.ToShort());
    }

    [Fact]
    public void LinkShouldPreferTheEntryTheIdNames()
    {
        // arrange
        var id = MentionNotification.New(OwnUserId, EntryId).Id;

        // act
        var link = Links.Notification(id, 77);
        var isChat = link.IsChat(out _, out long entryLid);

        // assert
        isChat.Should().BeTrue();
        entryLid.Should().Be(1234);
    }

    [Fact]
    public void LinkOfANotificationWithoutAChatShouldBeItsOwn()
    {
        // arrange
        var id = NotificationId.New(OwnUserId, NotificationKind.SpeechStarted, "anything");

        // act
        var link = Links.Notification(id);

        // assert
        link.Value.Should().StartWith("/n/").And.NotContain(OwnUserId.Value);
        link.IsNotification().Should().BeTrue();
        link.IsChat().Should().BeFalse();
        Links.TryParseNotification(OwnUserId, link.Value["/n/".Length..]).Should().Be(id);
    }

    [Fact]
    public void LinkShouldRoundTripForEveryChatKind()
    {
        // arrange
        var conversationId = ConversationId.New(GroupChatId, 1200);
        var callId = CallId.New(GroupChatId, "2067");
        var ids = new[] {
            MessageNotification.New(OwnUserId, GroupChatId).Id,
            MentionNotification.New(OwnUserId, EntryId).Id,
            ConversationNotification.New(OwnUserId, conversationId, 1230).Id,
            CallNotification.NewId(OwnUserId, callId),
        };

        foreach (var id in ids) {
            // act
            var link = Links.Notification(id);
            var isChat = link.IsChat(out var chatId, out long _);
            var shortId = GetQuery(link)[Links.NotificationIdQueryParameterName];
            var parsedId = Links.TryParseNotification(OwnUserId, shortId);

            // assert
            link.Value.Should().NotContain(" ");
            isChat.Should().BeTrue($"{link} is a chat link");
            chatId.Should().Be(GroupChatId);
            parsedId.Should().Be(id);
        }
    }

    [Theory]
    [InlineData(NotificationUIMode.Auto, null)]
    [InlineData(NotificationUIMode.Chats, "0")]
    [InlineData(NotificationUIMode.Notifications, "1")]
    public void LinkShouldSayWhereToOpenItOnlyWhenItMatters(NotificationUIMode ui, string? expected)
    {
        // arrange
        var id = MentionNotification.New(OwnUserId, EntryId).Id;

        // act
        var link = Links.WithNotification(Links.Chat(EntryId), id, ui);
        var query = GetQuery(link);

        // assert
        query[Links.NotificationUIQueryParameterName].Should().Be(expected);
        link.Value.Should().Contain("?n=1234&nid=", "the notification is added after the entry");
        link.IsChat(out _, out long entryLid).Should().BeTrue();
        entryLid.Should().Be(1234);
    }

    [Theory]
    [InlineData("auto", NotificationUIMode.Auto)]
    [InlineData("0", NotificationUIMode.Chats)]
    [InlineData("1", NotificationUIMode.Notifications)]
    [InlineData(null, NotificationUIMode.Auto)]
    [InlineData("", NotificationUIMode.Auto)]
    [InlineData("nonsense", NotificationUIMode.Auto)]
    public void UIModeShouldParse(string? value, NotificationUIMode expected)
    {
        // act
        var mode = NotificationUIModeExt.Parse(value);

        // assert
        mode.Should().Be(expected);
    }

    [Fact]
    public void UIModeShouldFormatAsItParses()
    {
        foreach (var mode in Enum.GetValues<NotificationUIMode>()) {
            // act
            var parsed = NotificationUIModeExt.Parse(mode.ToQueryValue());

            // assert
            parsed.Should().Be(mode);
        }
    }

    [Fact]
    public void PushLinkShouldNameTheNotificationAndLeaveTheUIToTheApp()
    {
        // arrange
        var notification = MentionNotification.New(OwnUserId, EntryId);

        // act
        var link = notification.GetChatLink();
        var query = GetQuery(link);

        // assert
        link.IsChat(out var chatId, out long entryLid).Should().BeTrue();
        chatId.Should().Be(GroupChatId);
        entryLid.Should().Be(1234);
        var parsedId = Links.TryParseNotification(OwnUserId, query[Links.NotificationIdQueryParameterName]);
        parsedId.Should().Be(notification.Id);
        query[Links.NotificationUIQueryParameterName].Should().BeNull("auto is the default");
    }

    [Fact]
    public void ConversationNotificationShouldLeadToItsFirstEntry()
    {
        // arrange
        var conversationId = ConversationId.New(GroupChatId, 1200);
        var id = ConversationNotification.New(OwnUserId, conversationId, 1230).Id;

        // act
        var hasTarget = id.TryGetChatTarget(out var chatId, out var entryLid);

        // assert
        hasTarget.Should().BeTrue();
        chatId.Should().Be(GroupChatId);
        entryLid.Should().Be(1200);
    }

    [Fact]
    public void CallNotificationShouldLeadToItsChat()
    {
        // arrange
        var id = CallNotification.NewId(OwnUserId, CallId.New(GroupChatId, "2067"));

        // act
        var hasTarget = id.TryGetChatTarget(out var chatId, out var entryLid);

        // assert
        hasTarget.Should().BeTrue();
        chatId.Should().Be(GroupChatId);
        entryLid.Should().Be(0);
    }

    [Fact]
    public void NotificationWithoutAChatShouldHaveNoTarget()
    {
        // arrange
        var id = NotificationId.New(OwnUserId, NotificationKind.SpeechStarted, "anything");

        // act
        var hasTarget = id.TryGetChatTarget(out var chatId, out _);

        // assert
        hasTarget.Should().BeFalse();
        chatId.Should().BeNull();
    }

    [Theory]
    [InlineData("/n", true, false, false)]
    [InlineData("/n/", true, false, false)]
    [InlineData("/n/1%3As-P7oXNDTeHL-752w3sfrad", false, true, false)]
    [InlineData("/chat/the-actual-one?nid=1%3Ax&nui=1", false, false, true)]
    [InlineData("/chat", false, false, false)]
    public void UrlKindsShouldNotBeMixedUp(string url, bool isRoot, bool isNotification, bool isChat)
    {
        // arrange
        var localUrl = new LocalUrl(url);

        // act & assert
        localUrl.IsNotificationRoot().Should().Be(isRoot);
        localUrl.IsNotification().Should().Be(isNotification);
        localUrl.IsChat().Should().Be(isChat);
    }

    private static System.Collections.Specialized.NameValueCollection GetQuery(LocalUrl url)
        => UriExt.GetQueryCollection(url.Value[url.Value.IndexOf('?')..]);
}
