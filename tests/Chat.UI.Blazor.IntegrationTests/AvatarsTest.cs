using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class AvatarsTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact(Skip = "TODO(DF): fix for CI")]
    public async Task CanCreateAnAvatar()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var command = new Avatars_Change(tester.Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "Test Avatar" }));
        var commander = tester.ClientServices.UICommander();
        var (avatar, error) = await commander.Run(command);
        avatar.Id.IsEmpty.Should().BeFalse();
        error.Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task DiffBasedUpdateAppliesOnlyChangedFields()
    {
        // Verifies the AvatarDiff pattern: updating only Name should not affect Bio or MediaId.

        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var commander = tester.Commander;

        // Create an avatar with specific Name and Bio
        var avatar = await commander.Call(new Avatars_Change(tester.Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "Original", Bio = "Original bio" })));
        avatar.Name.Should().Be("Original");
        avatar.Bio.Should().Be("Original bio");

        // Update only the Name via diff (Bio not set in diff → should remain unchanged)
        var updated = await commander.Call(new Avatars_Change(tester.Session, avatar.Id, null,
            Change.Update(new AvatarDiff { Name = "Updated" })));
        updated.Name.Should().Be("Updated");
        updated.Bio.Should().Be("Original bio"); // Unchanged!

        // Verify via a fresh read
        var avatars = appHost.Services.GetRequiredService<IAvatars>();
        var fresh = await avatars.GetOwn(tester.Session, avatar.Id, CancellationToken.None);
        fresh.Should().NotBeNull();
        fresh!.Name.Should().Be("Updated");
        fresh.Bio.Should().Be("Original bio");
    }

    [Fact(Timeout = 30_000)]
    public async Task DiffBasedUpdateSucceedsAfterConcurrentModification()
    {
        // Verifies fix for bug #3736: diff-based update with null version
        // succeeds even when the avatar was concurrently modified on the server.

        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var commander = tester.Commander;

        // Create an avatar
        var avatar = await commander.Call(new Avatars_Change(tester.Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "Original" })));

        // Simulate a concurrent server-side modification (e.g. migration flow changing MediaId)
        var avatarsBackend = appHost.Services.GetRequiredService<IAvatarsBackend>();
        await commander.Call(new AvatarsBackend_Change(avatar.Id, null,
            Change.Update(new AvatarDiff { Bio = "Modified by migration" })));

        // Update Name via diff with null version — should succeed
        var updated = await commander.Call(new Avatars_Change(tester.Session, avatar.Id, null,
            Change.Update(new AvatarDiff { Name = "Updated name" })));
        updated.Name.Should().Be("Updated name");
        updated.Bio.Should().Be("Modified by migration"); // Migration change preserved!
    }
}
