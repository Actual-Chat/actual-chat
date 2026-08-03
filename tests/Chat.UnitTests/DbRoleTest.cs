using ActualChat.Chat.Db;

namespace ActualChat.Chat.UnitTests;

public class DbRoleTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void ModerateSurvivesRoundTrip()
    {
        // arrange
        var role = NewRole(SystemRole.None, ChatPermissions.Moderate.AddImplied());

        // act
        var restored = new DbRole(role).ToModel();

        // assert
        restored.Permissions.Has(ChatPermissions.Moderate).Should().BeTrue();
        restored.Permissions.Has(ChatPermissions.EditProperties).Should().BeTrue();
        restored.Permissions.Has(ChatPermissions.EditMembers).Should().BeTrue();
    }

    [Fact]
    public void AbsentModerateSurvivesRoundTrip()
    {
        // arrange
        var role = NewRole(SystemRole.None, ChatPermissions.Write.AddImplied());

        // act
        var restored = new DbRole(role).ToModel();

        // assert
        restored.Permissions.Has(ChatPermissions.Moderate).Should().BeFalse();
        restored.Permissions.Has(ChatPermissions.Write).Should().BeTrue();
    }

    [Fact]
    public void CanModerateColumnTracksThePermission()
    {
        // act
        var withModerate = new DbRole(NewRole(SystemRole.None, ChatPermissions.Moderate.AddImplied()));
        var withoutModerate = new DbRole(NewRole(SystemRole.None, ChatPermissions.Write.AddImplied()));

        // assert
        withModerate.CanModerate.Should().BeTrue();
        withoutModerate.CanModerate.Should().BeFalse();
    }

    [Fact]
    public void ModeratorSystemRoleIsClampedOnRead()
    {
        // arrange - a stored Moderator row whose columns were widened out of band
        var role = NewRole(SystemRole.Moderator, ChatPermissions.Owner.AddImplied());

        // act
        var restored = new DbRole(role).ToModel();

        // assert
        restored.Permissions.Should().Be(ChatPermissionsExt.Moderator.AddImplied());
        restored.Permissions.Has(ChatPermissions.Owner).Should().BeFalse();
        restored.Permissions.Has(ChatPermissions.EditRoles).Should().BeFalse();
        restored.Permissions.Has(ChatPermissions.Moderate).Should().BeTrue();
    }

    [Fact]
    public void ModeratorSystemRoleIsClampedUpOnRead()
    {
        // arrange - a Moderator row predating the CanModerate column, so it defaulted to false
        var role = NewRole(SystemRole.Moderator, ChatPermissions.Read);

        // act
        var restored = new DbRole(role).ToModel();

        // assert
        restored.Permissions.Has(ChatPermissions.Moderate).Should().BeTrue();
    }

    // Private methods

    private static Role NewRole(SystemRole systemRole, ChatPermissions permissions)
        => new (RoleId.New(TestChatId, 1), 1) {
            SystemRole = systemRole,
            Name = "test role",
            Permissions = permissions,
        };
}
