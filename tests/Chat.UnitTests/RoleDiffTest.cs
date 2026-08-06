
namespace ActualChat.Chat.UnitTests;

public class RoleDiffTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var engine = DiffEngine.Default;
        var chatId = ChatId.Parse("chatid");
        var role = new Role(RoleId.New(chatId, 10L), 1L);
        var createAnyoneRoleCmd = new RolesBackend_Change(chatId,
            default,
            null,
            new () {
                Create = new RoleDiff() {
                    SystemRole = SystemRole.Anyone,
                    Permissions =
                        ChatPermissions.Write
                        | ChatPermissions.Invite
                        | ChatPermissions.SeeMembers
                        | ChatPermissions.Leave,
                },
            });
        var change = createAnyoneRoleCmd.Change.Create.Value;
        var role2 = engine.Patch(role, change);
        WriteLine(change.ToString());
        WriteLine(role2.ToString());
    }
}
