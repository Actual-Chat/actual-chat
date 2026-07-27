using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class AvatarsTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private Session Session => Tester.Session;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldCreateAvatar()
    {
        // arrange
        await Tester.SignInAsUniqueBob();

        // act
        var avatar = await Tester.Commander.Call(new Avatars_Change(Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "Test Avatar" })));

        // assert
        avatar.Id.IsEmpty.Should().BeFalse();
        avatar.Name.Should().Be("Test Avatar");
    }

    [Fact(Timeout = 30_000)]
    public async Task CreatedAvatarBelongsToCurrentAccount()
    {
        // arrange
        await using var other = AppHost.NewWebClientTester(Out);
        var account = await Tester.SignInAsUniqueBob();
        var otherAccount = await other.SignInAsUniqueAlice();

        // act
        var avatar = await Tester.Commander.Call(new Avatars_Change(Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff {
                Name = "Test Avatar",
                UserId = otherAccount.Id,
            })));

        // assert
        avatar.UserId.Should().Be(account.Id);
        avatar.UserId.Should().NotBe(otherAccount.Id);
    }

    [Fact(Timeout = 30_000)]
    public async Task CannotSelectAvatarOfAnotherAccount()
    {
        // arrange
        await using var other = AppHost.NewWebClientTester(Out);
        await Tester.SignInAsUniqueBob();
        await other.SignInAsUniqueAlice();
        var ownAvatar = await Tester.Commander.Call(new Avatars_Change(Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "Own Avatar" })));
        var otherAvatar = await other.Commander.Call(new Avatars_Change(other.Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "Other Avatar" })));
        var (chatId, _) = await Tester.CreateChat(true);

        // act
        var exception = await Record.ExceptionAsync(
            () => Tester.Commander.Call(new Authors_SetAvatar(Session, chatId, otherAvatar.Id)));
        await Tester.Commander.Call(new Authors_SetAvatar(Session, chatId, ownAvatar.Id));
        var author = await Tester.Authors.GetOwn(Session, chatId, default).Require();

        // assert
        exception.Should().BeAssignableTo<NotFoundException>();
        author.AvatarId.Should().Be(ownAvatar.Id);
    }

    [Fact(Timeout = 30_000)]
    public async Task ShouldApplyOnlyChangedFieldsOnUpdate()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var avatar = await Tester.Commander.Call(new Avatars_Change(Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "Original", Bio = "Original bio" })));

        // act
        var updated = await Tester.Commander.Call(new Avatars_Change(Session, avatar.Id, null,
            Change.Update(new AvatarDiff { Name = "Updated" })));

        // assert
        updated.Name.Should().Be("Updated");
        updated.Bio.Should().Be("Original bio"); // Unchanged!

        var fresh = await Tester.Avatars.GetOwn(Session, avatar.Id, default);
        fresh.Should().NotBeNull();
        fresh.Name.Should().Be("Updated");
        fresh.Bio.Should().Be("Original bio");
    }

    [Fact(Timeout = 30_000)]
    public async Task ShouldUpdateAfterConcurrentModification()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var avatar = await Tester.Commander.Call(new Avatars_Change(Session, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "Original" })));
        await Commander.Call(new AvatarsBackend_Change(avatar.Id, null,
            Change.Update(new AvatarDiff { Bio = "Modified by migration" })));

        // act
        var updated = await Tester.Commander.Call(new Avatars_Change(Session, avatar.Id, null,
            Change.Update(new AvatarDiff { Name = "Updated name" })));

        // assert
        updated.Name.Should().Be("Updated name");
        updated.Bio.Should().Be("Modified by migration"); // Migration change preserved!
    }
}
