using ActualChat.Invite;

namespace ActualChat.Chat.UnitTests;

public class InviteDetailsSerializationTest
{
    [Fact]
    public void BasicTest()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var serializer = SystemJsonSerializer.Default.ToTyped<InviteDetails>();
        var chatInviteOption = new ChatInviteOption(chatId);
        var inviteDetails = (InviteDetails)chatInviteOption;
        var detailsJson = serializer.Write(inviteDetails);
        detailsJson.Should().NotBeNull();
        detailsJson.Should().Contain(chatId.Value);
        var inviteDetails2 = serializer.Read(detailsJson);
        inviteDetails2.Should().NotBeNull();
        inviteDetails2.Chat.Should().NotBeNull();
        inviteDetails2.Should().Be(inviteDetails);
    }
}
