using ActualChat.Invite;

namespace ActualChat.Chat.UnitTests;

public class InviteDetailsSerializationTest
{
    [Fact]
    public void BasicTest()
    {
        const string chatId = "r5IbjdG7Cq";
        var serializer = SystemJsonSerializer.Default.ToTyped<InviteDetails>();
        var chatInviteOption = new ChatInviteOption(new ChatId(chatId));
        var inviteDetails = (InviteDetails)chatInviteOption;
        var detailsJson = serializer.Write(inviteDetails);
        detailsJson.Should().NotBeNull();
        detailsJson.Should().Contain(chatId);
        var inviteDetails2 = serializer.Read(detailsJson);
        inviteDetails2.Should().NotBeNull();
        inviteDetails2.Chat.Should().NotBeNull();
        inviteDetails2.Should().Be(inviteDetails);
    }
}
