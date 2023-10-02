using ActualChat.Diff;

namespace ActualChat.Chat.UnitTests;

public class RoleDiffTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var engine = DiffEngine.Default;
        var chatId = new ChatId("chatid");
        var role = new Role(new RoleId(chatId, 10L, AssumeValid.Option), 1L);
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
        Out.WriteLine(change.ToString());
        Out.WriteLine(role2.ToString());
    }
}
