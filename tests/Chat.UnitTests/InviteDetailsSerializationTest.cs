using ActualChat.Invite;
using ChatModel = ActualChat.Chat.Chat;

namespace ActualChat.Chat.UnitTests;

public class InviteDetailsSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();

    [Fact]
    public void InviteDetails_ChatInvite()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var chatInviteOption = new ChatInviteOption(chatId);
        InviteDetails inviteDetails = chatInviteOption;
        inviteDetails.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void InviteDetails_PlaceInvite()
    {
        var placeId = PlaceId.New();
        var placeInviteOption = new PlaceInviteOption(placeId);
        InviteDetails inviteDetails = placeInviteOption;
        inviteDetails.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Invite_Basic()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var invite = new Invite.Invite("invite-1", 1) {
            CreatedBy = "admin",
            CreatedAt = new Moment(DateTime.UtcNow),
            ExpiresOn = new Moment(DateTime.UtcNow) + TimeSpan.FromDays(7),
            Remaining = 10,
            Details = new ChatInviteOption(chatId),
        };

        var s = invite.PassThroughAllSerializers(Out);
        s.Id.Should().Be(invite.Id);
        s.CreatedBy.Should().Be(invite.CreatedBy);
        s.Remaining.Should().Be(invite.Remaining);
        s.Details.Chat.Should().NotBeNull();
    }

    [Fact]
    public void InviteChatLinkPreview_Basic()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var chat = new ChatModel(chatId) { Title = "Test Chat" };
        var preview = new InviteChatLinkPreview(chat, null);

        var s = preview.PassThroughAllSerializers(Out);
        s.Chat.Should().NotBeNull();
        s.Chat!.Id.Should().Be(chatId);
    }

    // API Commands

    [Fact]
    public void Invites_Generate_Basic()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var invite = new Invite.Invite("invite-1") {
            Details = new ChatInviteOption(chatId),
            Remaining = 5,
        };
        var cmd = new Invites_Generate(TestSession, invite);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.Invite.Id.Should().Be(original.Invite.Id);
            }, Out);
    }

    [Fact]
    public void Invites_Use_Basic()
    {
        var cmd = new Invites_Use(TestSession, "invite-1");
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Invites_Revoke_Basic()
    {
        var cmd = new Invites_Revoke(TestSession, "invite-1");
        cmd.AssertPassesThroughAllSerializers();
    }

    // Backend Commands

    [Fact]
    public void InvitesBackend_Generate_Basic()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var invite = new Invite.Invite("invite-1") {
            Details = new ChatInviteOption(chatId),
            Remaining = 5,
        };
        var cmd = new InvitesBackend_Generate(invite);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Invite.Id.Should().Be(original.Invite.Id);
            }, Out);
    }

    [Fact]
    public void InvitesBackend_Use_Basic()
    {
        var cmd = new InvitesBackend_Use(TestSession, "invite-1");
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void InvitesBackend_Revoke_Basic()
    {
        var cmd = new InvitesBackend_Revoke(TestSession, "invite-1");
        cmd.AssertPassesThroughAllSerializers();
    }
}
