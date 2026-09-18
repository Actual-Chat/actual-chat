using ActualChat.WebHooks;

namespace ActualChat.Chat.UnitTests;

public class WebHookModelSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void WebHook_Basic()
    {
        var webHookId = WebHookId.New();
        var chatId = ChatId.Parse("the-actual-one");
        var webHook = new WebHook(webHookId, 1) {
            Scope = WebHookScope.Chat,
            ScopeId = chatId.Value,
            Kind = WebHookKind.Outgoing,
            Name = "Test hook",
            CreatedBy = UserId.New(),
            CreatedAt = new Moment(DateTime.UtcNow),
            ModifiedAt = new Moment(DateTime.UtcNow),
            IsEnabled = true,
            DisabledReason = WebHookDisabledReason.None,
            LastActivityAt = new Moment(DateTime.UtcNow),
            Url = "https://example.com/hook",
            Events = WebHookEvents.MessagePosted | WebHookEvents.Ping,
            IncludeText = true,
            ChatIds = [chatId],
            SubscribeNotifications = true,
            CustomHeaderName = "X-My-Header",
            ConsecutiveFailures = 2,
            LastStatusCode = 200,
            LastError = "none",
        };

        var s = webHook.PassThroughSerializers(Out);
        s.Id.Should().Be(webHook.Id);
        s.Version.Should().Be(webHook.Version);
        s.Scope.Should().Be(webHook.Scope);
        s.ScopeId.Should().Be(webHook.ScopeId);
        s.Kind.Should().Be(webHook.Kind);
        s.Name.Should().Be(webHook.Name);
        s.CreatedBy.Should().Be(webHook.CreatedBy);
        s.CreatedAt.Should().Be(webHook.CreatedAt);
        s.ModifiedAt.Should().Be(webHook.ModifiedAt);
        s.IsEnabled.Should().Be(webHook.IsEnabled);
        s.DisabledReason.Should().Be(webHook.DisabledReason);
        s.LastActivityAt.Should().Be(webHook.LastActivityAt);
        s.Url.Should().Be(webHook.Url);
        s.Events.Should().Be(webHook.Events);
        s.IncludeText.Should().Be(webHook.IncludeText);
        s.ChatIds.Should().BeEquivalentTo(webHook.ChatIds);
        s.SubscribeNotifications.Should().Be(webHook.SubscribeNotifications);
        s.CustomHeaderName.Should().Be(webHook.CustomHeaderName);
        s.ConsecutiveFailures.Should().Be(webHook.ConsecutiveFailures);
        s.LastStatusCode.Should().Be(webHook.LastStatusCode);
        s.LastError.Should().Be(webHook.LastError);
        s.IsActiveOutgoing.Should().Be(webHook.IsActiveOutgoing);
    }

    [Fact]
    public void WebHookDiff_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var diff = new WebHookDiff {
            Name = "Updated hook",
            Url = "https://example.com/hook2",
            Events = WebHookEvents.MessageEdited,
            IncludeText = false,
            ChatIds = [chatId],
            SubscribeNotifications = false,
            CustomHeaderName = "X-Other",
            CustomHeaderValue = "secret",
            IsEnabled = false,
        };

        var s = diff.PassThroughSerializers(Out);
        s.Name.Should().Be(diff.Name);
        s.Url.Should().Be(diff.Url);
        s.Events.Should().Be(diff.Events);
        s.IncludeText.Should().Be(diff.IncludeText);
        s.ChatIds.Should().BeEquivalentTo(diff.ChatIds);
        s.SubscribeNotifications.Should().Be(diff.SubscribeNotifications);
        s.CustomHeaderName.Should().Be(diff.CustomHeaderName);
        s.CustomHeaderValue.Should().Be(diff.CustomHeaderValue);
        s.IsEnabled.Should().Be(diff.IsEnabled);
    }

    [Fact]
    public void WebHookDelivery_Basic()
    {
        var webHookId = WebHookId.New();
        var delivery = new WebHookDelivery("delivery-1", webHookId) {
            Seq = 42,
            EventType = "message.posted",
            Status = WebHookDeliveryStatus.Succeeded,
            Attempts = 1,
            NextAttemptAt = new Moment(DateTime.UtcNow),
            LastStatusCode = 200,
            LastError = null,
            LastLatencyMs = 123,
            CreatedAt = new Moment(DateTime.UtcNow),
            CompletedAt = new Moment(DateTime.UtcNow),
        };

        delivery.AssertPassesThroughSerializers(Out);
    }

    [Fact]
    public void WebHookChangeResult_Basic()
    {
        var webHookId = WebHookId.New();
        var webHook = new WebHook(webHookId, 1) {
            Scope = WebHookScope.User,
            ScopeId = UserId.New().Value,
        };
        var result = new WebHookChangeResult(webHook, "secret-value");

        var s = result.PassThroughSerializers(Out);
        s.WebHook.Should().NotBeNull();
        s.WebHook!.Id.Should().Be(webHook.Id);
        s.Secret.Should().Be(result.Secret);
    }

    [Theory]
    [InlineData(WebHookEvents.MessagePosted, "message.posted")]
    [InlineData(WebHookEvents.PlaceMemberJoined, "place.member.joined")]
    [InlineData(WebHookEvents.Ping, "ping")]
    public void EventTypeShouldBeDottedLowerCase(WebHookEvents e, string expected)
        => e.ToEventType().Should().Be(expected);
}
