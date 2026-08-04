using ActualChat.Invite;
using ChatModel = ActualChat.Chat.Chat;

namespace ActualChat.Chat.UnitTests;

public class InviteDetailsSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();

    [Fact]
    public void ChatInvite_Basic()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var invite = new ChatInvite("invite-1", 1) {
            CreatedBy = "admin",
            CreatedAt = new Moment(DateTime.UtcNow),
            ExpiresOn = new Moment(DateTime.UtcNow) + TimeSpan.FromDays(7),
            Remaining = 10,
            ChatId = chatId,
        };
        var s = (ChatInvite)((ActualChat.Invite.Invite)invite).PassThroughModernSerializers(Out);
        s.Id.Should().Be(invite.Id);
        s.CreatedBy.Should().Be(invite.CreatedBy);
        s.Remaining.Should().Be(invite.Remaining);
        s.ChatId.Should().Be(chatId);
    }

    [Fact]
    public void PlaceInvite_Basic()
    {
        var placeId = PlaceId.New();
        ActualChat.Invite.Invite invite = new PlaceInvite("invite-2", 1) {
            Remaining = 5,
            PlaceId = placeId,
        };
        var s = invite.PassThroughModernSerializers(Out);
        s.Should().BeOfType<PlaceInvite>();
        ((PlaceInvite)s).PlaceId.Should().Be(placeId);
    }

    [Fact]
    public void InviteChatLinkPreview_Basic()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var chat = new ChatModel(chatId) { Title = "Test Chat" };
        var preview = new InviteChatLinkPreview(chat, null);

        var s = preview.PassThroughSerializers(Out);
        s.Chat.Should().NotBeNull();
        s.Chat!.Id.Should().Be(chatId);
    }

    // API Commands

    [Fact]
    public void Invites_Generate_Basic()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        ActualChat.Invite.Invite invite = new ChatInvite("invite-1") {
            ChatId = chatId,
            Remaining = 5,
        };
        var cmd = new Invites_Generate(TestSession, invite);
        var s = cmd.PassThroughModernSerializers(Out);
        s.Session.Should().Be(cmd.Session);
        s.Invite.Id.Should().Be(cmd.Invite.Id);
    }

    [Fact]
    public void Invites_Use_Basic()
    {
        var cmd = new Invites_Use(TestSession, "invite-1");
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Invites_Revoke_Basic()
    {
        var cmd = new Invites_Revoke(TestSession, "invite-1");
        cmd.AssertPassesThroughSerializers();
    }

    // Backend Commands

    [Fact]
    public void InvitesBackend_Generate_Basic()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        ActualChat.Invite.Invite invite = new ChatInvite("invite-1") {
            ChatId = chatId,
            Remaining = 5,
        };
        var cmd = new InvitesBackend_Generate(invite);
        var s = cmd.PassThroughModernSerializers(Out);
        s.Invite.Id.Should().Be(cmd.Invite.Id);
    }

    [Fact]
    public void InvitesBackend_Use_Basic()
    {
        var cmd = new InvitesBackend_Use(TestSession, "invite-1");
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void InvitesBackend_Revoke_Basic()
    {
        var cmd = new InvitesBackend_Revoke(TestSession, "invite-1");
        cmd.AssertPassesThroughSerializers();
    }
}
