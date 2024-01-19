using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

public class AvatarsTest : AppHostTestBase
{
    public AvatarsTest(ITestOutputHelper @out) : base(@out) { }

    [Fact(Skip = "TODO(DF): fix for CI")]
    public async Task CanCreateAnAvatar()
    {
        // Tests that we can create an avatar via web api call (AvatarsController.Change).
        // There was a problem that model validation failed because framework
        // tried to access ChangeCommand.Change.Update.Value while ActualLab.Option had no value
        // and this caused an exception.
        // The issue was solved by adding ActualChat.Web.Internal.OptionPropsValidationFilter
        // which excludes accessing ActualLab.Option.Value property during model validation.

        using var appHost = await NewAppHost();
        await using var tester = appHost.NewWebClientTester();
        var account = await tester.SignInAsBob("no-admin");
        var command = new Avatars_Change(tester.Session, Symbol.Empty, null, new Change<AvatarFull>() {
            Create = new AvatarFull(account.Id),
        });
        var commander = tester.ClientServices.UICommander();
        var (avatar, error) = await commander.Run(command);
        avatar.Id.IsEmpty.Should().BeFalse();
        error.Should().BeNull();
    }
}
