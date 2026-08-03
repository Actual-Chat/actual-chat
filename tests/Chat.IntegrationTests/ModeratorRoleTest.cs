using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ModeratorRoleTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Moderator => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Member => field ??= fixture.AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await Moderator.DisposeSilentlyAsync();
        await Member.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ModeratorGainsModeratePermission()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();

        // act
        await PromoteToModerator(moderatorAuthorId);

        // assert
        var rules = await GetRules(Moderator, chatId);
        rules.CanModerate().Should().BeTrue();
        rules.CanEditProperties().Should().BeTrue();
        rules.CanEditMembers().Should().BeTrue();
        rules.IsOwner().Should().BeFalse();
        rules.CanEditRoles().Should().BeFalse();

        var memberRules = await GetRules(Member, chatId);
        memberRules.CanModerate().Should().BeFalse();
    }

    [Fact]
    public async Task ModeratorIsListedAndCanBeDemoted()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        var roles = Owner.AppServices.GetRequiredService<IRoles>();

        // act
        await PromoteToModerator(moderatorAuthorId);
        var afterPromote = await roles.ListModeratorIds(Owner.Session, chatId, default);
        await Owner.Commander.Call(
            new Authors_ChangeRole(Owner.Session, moderatorAuthorId, SystemRole.Moderator, false));
        var afterDemote = await roles.ListModeratorIds(Owner.Session, chatId, default);

        // assert
        afterPromote.Should().Contain(moderatorAuthorId);
        afterDemote.Should().NotContain(moderatorAuthorId);
        var rules = await GetRules(Moderator, chatId);
        rules.CanModerate().Should().BeFalse();
    }

    [Fact]
    public async Task ModeratorCanRemoveOthersMessagesButNotOwners()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var memberEntry = await Member.Commander.Call(
            new Chats_UpsertEntry(Member.Session, chatId, null) { Text = "member message" });
        var ownerEntry = await Owner.Commander.Call(
            new Chats_UpsertEntry(Owner.Session, chatId, null) { Text = "owner message" });

        // act
        await Moderator.Commander.Call(
            new Chats_RemoveEntry(Moderator.Session, chatId, memberEntry.LocalId));
        var removeOwnerEntry = () => Moderator.Commander
            .Call(new Chats_RemoveEntry(Moderator.Session, chatId, ownerEntry.LocalId));

        // assert
        await removeOwnerEntry.Should().ThrowAsync<Exception>();
        var chats = Moderator.AppServices.GetRequiredService<IChats>();
        var removed = await chats.GetEntry(Moderator.Session, memberEntry.Id, default);
        removed.Should().BeNull();
    }

    [Fact]
    public async Task PlainMemberCannotRemoveOthersMessages()
    {
        // arrange
        var (chatId, _, _) = await ArrangeChat();
        var ownerEntry = await Owner.Commander.Call(
            new Chats_UpsertEntry(Owner.Session, chatId, null) { Text = "owner message" });

        // act
        var removeOwnerEntry = () => Member.Commander
            .Call(new Chats_RemoveEntry(Member.Session, chatId, ownerEntry.LocalId));

        // assert
        await removeOwnerEntry.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ModeratorCanRestoreRemovedMessages()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var memberEntry = await Member.Commander.Call(
            new Chats_UpsertEntry(Member.Session, chatId, null) { Text = "member message" });
        await Member.Commander.Call(new Chats_RemoveEntry(Member.Session, chatId, memberEntry.LocalId));

        // act
        await Moderator.Commander.Call(
            new Chats_RestoreEntry(Moderator.Session, chatId, memberEntry.LocalId));

        // assert
        var chats = Moderator.AppServices.GetRequiredService<IChats>();
        var restored = await chats.GetEntry(Moderator.Session, memberEntry.Id, default);
        restored.Should().NotBeNull();
    }

    [Fact]
    public async Task ModeratorCanKickMemberButNotOwner()
    {
        // arrange
        var (chatId, moderatorAuthorId, memberAuthorId) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var ownerAuthorId = (await Owner.GetOwnAuthor(chatId).Require()).Id;

        // act
        await Moderator.Commander.Call(new Authors_Exclude(Moderator.Session, memberAuthorId));
        var kickOwner = () => Moderator.Commander.Call(new Authors_Exclude(Moderator.Session, ownerAuthorId));

        // assert
        await kickOwner.Should().ThrowAsync<Exception>();
        var authors = Owner.AppServices.GetRequiredService<IAuthors>();
        var kicked = await authors.Get(Owner.Session, chatId, memberAuthorId, default);
        kicked!.HasLeft.Should().BeTrue();
    }

    [Fact]
    public async Task ModeratorCannotKickAnonymousOwner()
    {
        // arrange - IRoles.ListOwnerIds masks anonymous owners from non-owner callers, so the
        // exclusion guard has to resolve them through the unmasked backend path instead.
        await Owner.SignInAsUniqueBob();
        await Moderator.SignInAsUniqueAlice();
        await Member.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(x => x with {
            IsPublic = false,
            AllowAnonymousAuthors = true,
        });
        var moderatorAuthor = await Moderator.JoinChat(chatId, inviteId);
        var anonymousAuthor = await Member.JoinChat(chatId, inviteId, true);
        await Owner.Commander.Call(
            new Authors_ChangeRole(Owner.Session, anonymousAuthor.Id, SystemRole.Owner, true));
        await PromoteToModerator(moderatorAuthor.Id);

        // act
        var kickAnonymousOwner = () => Moderator.Commander
            .Call(new Authors_Exclude(Moderator.Session, anonymousAuthor.Id));

        // assert
        await kickAnonymousOwner.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task PlaceModeratorModeratesPublicPlaceChats()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var moderatorAccount = await Moderator.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(false, "moderated place", moderatorAccount);
        var placeId = place.Id;
        await Moderator.JoinPlace(placeId);
        var moderatorAuthor = await Moderator.GetOwnAuthor(placeId.RootChatId).Require();
        var (publicChatId, _) = await Owner.CreateChat(true, "public place chat", placeId);
        var (privateChatId, _) = await Owner.CreateChat(false, "private place chat", placeId);

        // act - appointed once on the place root chat
        await Owner.Commander.Call(
            new Places_ChangeRole(Owner.Session, moderatorAuthor.Id, SystemRole.Moderator, true));

        // assert
        var rootRules = await WaitForModerate(Moderator, placeId.RootChatId, true);
        rootRules.Should().BeTrue();
        var publicRules = await WaitForModerate(Moderator, publicChatId, true);
        publicRules.Should().BeTrue();
        var privateRules = await WaitForModerate(Moderator, privateChatId, false);
        privateRules.Should().BeFalse();

        var places = Owner.AppServices.GetRequiredService<IPlaces>();
        var moderatorIds = await places.ListModeratorIds(Owner.Session, placeId, default);
        moderatorIds.Should().Equal(moderatorAuthor.Id);
    }

    [Fact]
    public async Task ModeratorCanEditTitleAndDescriptionButNotChatType()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);

        // act
        var updated = await Moderator.Commander.Call(new Chats_Change(Moderator.Session,
            chatId,
            null,
            Change.Update(new ChatDiff { Title = "moderated title", Description = "moderated description" })));
        var makePublic = () => Moderator.Commander.Call(new Chats_Change(Moderator.Session,
            chatId,
            null,
            Change.Update(new ChatDiff { IsPublic = true })));
        var archive = () => Moderator.Commander.Call(new Chats_Change(Moderator.Session,
            chatId,
            null,
            Change.Update(new ChatDiff { IsArchived = true })));
        var remove = () => Moderator.Commander.Call(
            new Chats_Change(Moderator.Session, chatId, null, Change.Remove(new ChatDiff())));

        // assert
        updated.Title.Should().Be("moderated title");
        updated.Description.Should().Be("moderated description");
        await makePublic.Should().ThrowAsync<Exception>();
        await archive.Should().ThrowAsync<Exception>();
        await remove.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ModeratorCannotAppointRoles()
    {
        // arrange
        var (_, moderatorAuthorId, memberAuthorId) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);

        // act
        var appointModerator = () => Moderator.Commander.Call(
            new Authors_ChangeRole(Moderator.Session, memberAuthorId, SystemRole.Moderator, true));
        var appointOwner = () => Moderator.Commander.Call(
            new Authors_ChangeRole(Moderator.Session, memberAuthorId, SystemRole.Owner, true));

        // assert
        await appointModerator.Should().ThrowAsync<Exception>();
        await appointOwner.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ChangeRoleRejectsAutomaticRolesAndOwnerDemotion()
    {
        // arrange
        var (_, moderatorAuthorId, _) = await ArrangeChat();

        // act
        var setAnyone = () => Owner.Commander.Call(
            new Authors_ChangeRole(Owner.Session, moderatorAuthorId, SystemRole.Anyone, true));
        var setNone = () => Owner.Commander.Call(
            new Authors_ChangeRole(Owner.Session, moderatorAuthorId, SystemRole.None, true));
        var demoteOwner = () => Owner.Commander.Call(
            new Authors_ChangeRole(Owner.Session, moderatorAuthorId, SystemRole.Owner, false));

        // assert
        await setAnyone.Should().ThrowAsync<Exception>();
        await setNone.Should().ThrowAsync<Exception>();
        await demoteOwner.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task PromotingModeratorToOwnerDropsModeratorRole()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var roles = Owner.AppServices.GetRequiredService<IRoles>();

        // act
        await Owner.Commander.Call(
            new Authors_ChangeRole(Owner.Session, moderatorAuthorId, SystemRole.Owner, true));

        // assert
        var ownerIds = await roles.ListOwnerIds(Owner.Session, chatId, default);
        var moderatorIds = await roles.ListModeratorIds(Owner.Session, chatId, default);
        ownerIds.Should().Contain(moderatorAuthorId);
        moderatorIds.Should().NotContain(moderatorAuthorId);
    }

    [Fact]
    [Obsolete("2026.08: Use Authors_ChangeRole. Old clients only.")]
    public async Task LegacyPromoteToOwnerCommandStillWorks()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var roles = Owner.AppServices.GetRequiredService<IRoles>();

        // act
        await Owner.Commander.Call(new Authors_PromoteToOwner(Owner.Session, moderatorAuthorId));

        // assert - the shim rewrites into Authors_ChangeRole, so it inherits its behaviour
        var ownerIds = await roles.ListOwnerIds(Owner.Session, chatId, default);
        var moderatorIds = await roles.ListModeratorIds(Owner.Session, chatId, default);
        ownerIds.Should().Contain(moderatorAuthorId);
        moderatorIds.Should().NotContain(moderatorAuthorId);
    }

    [Fact]
    public async Task LeavingChatDropsModeratorRole()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var roles = Owner.AppServices.GetRequiredService<IRoles>();

        // act
        await Moderator.Commander.Call(new Authors_Leave(Moderator.Session, chatId));

        // assert
        var moderatorIds = await roles.ListModeratorIds(Owner.Session, chatId, default);
        moderatorIds.Should().NotContain(moderatorAuthorId);
    }

    [Fact]
    public async Task TemplateChatCloneCarriesModeratorRole()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        await Moderator.SignInAsUniqueAlice();
        await Member.SignInAsUniqueAlice();
        var (templateChatId, _) = await Owner.CreateChat(x => x with { IsPublic = true, IsTemplate = true });
        var moderatorAuthor = await Moderator.JoinChat(templateChatId, Symbol.Empty);
        await PromoteToModerator(moderatorAuthor.Id);

        // act
        var clone = await Member.Commander.Call(
            new Chats_GetOrCreateFromTemplate(Member.Session, templateChatId));

        // assert
        clone.Id.Should().NotBe(templateChatId);
        var roles = Owner.AppServices.GetRequiredService<IRoles>();
        var clonedModeratorIds = await roles.ListModeratorIds(Owner.Session, clone.Id, default);
        clonedModeratorIds.Should().HaveCount(1);

        var clonedModeratorAuthor = await Moderator.GetOwnAuthor(clone.Id).Require();
        clonedModeratorIds.Should().Equal(clonedModeratorAuthor.Id);
        var clonedRules = await GetRules(Moderator, clone.Id);
        clonedRules.CanModerate().Should().BeTrue();
    }

    [Fact]
    public async Task PlainMemberCannotEditChatProperties()
    {
        // arrange
        var (chatId, _, _) = await ArrangeChat();

        // act
        var editTitle = () => Member.Commander.Call(new Chats_Change(Member.Session,
            chatId,
            null,
            Change.Update(new ChatDiff { Title = "hijacked title" })));
        var editDescription = () => Member.Commander.Call(new Chats_Change(Member.Session,
            chatId,
            null,
            Change.Update(new ChatDiff { Description = "hijacked description" })));

        // assert
        await editTitle.Should().ThrowAsync<Exception>();
        await editDescription.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task PlainMemberCannotKickMembers()
    {
        // arrange
        var (_, moderatorAuthorId, _) = await ArrangeChat();

        // act
        var kick = () => Member.Commander.Call(new Authors_Exclude(Member.Session, moderatorAuthorId));

        // assert
        await kick.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task PlainMemberCannotRestoreOthersRemovedMessages()
    {
        // arrange
        var (chatId, _, _) = await ArrangeChat();
        var ownerEntry = await Owner.Commander.Call(
            new Chats_UpsertEntry(Owner.Session, chatId, null) { Text = "owner message" });
        await Owner.Commander.Call(new Chats_RemoveEntry(Owner.Session, chatId, ownerEntry.LocalId));

        // act
        var restore = () => Member.Commander
            .Call(new Chats_RestoreEntry(Member.Session, chatId, ownerEntry.LocalId));

        // assert
        await restore.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task PlainMemberCannotAppointRoles()
    {
        // arrange
        var (_, moderatorAuthorId, memberAuthorId) = await ArrangeChat();

        // act
        var appointOther = () => Member.Commander.Call(
            new Authors_ChangeRole(Member.Session, moderatorAuthorId, SystemRole.Moderator, true));
        var appointSelf = () => Member.Commander.Call(
            new Authors_ChangeRole(Member.Session, memberAuthorId, SystemRole.Moderator, true));

        // assert
        await appointOther.Should().ThrowAsync<Exception>();
        await appointSelf.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task NonMemberSeesNoPrivilegedMembers()
    {
        // arrange - the listing methods are gated on CanSeeMembers
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var outsider = fixture.AppHost.NewWebClientTester(Out);
        await using var _ = outsider.ConfigureAwait(false);
        await outsider.SignInAsUniqueAlice();
        var roles = outsider.AppServices.GetRequiredService<IRoles>();

        // act
        var moderatorIds = await roles.ListModeratorIds(outsider.Session, chatId, default);
        var ownerIds = await roles.ListOwnerIds(outsider.Session, chatId, default);

        // assert
        moderatorIds.Should().BeEmpty();
        ownerIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ModeratorCannotChangeRoles()
    {
        // arrange - Roles_Change is the direct route to granting oneself Owner permissions
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);

        // act
        var createRole = () => Moderator.Commander.Call(new Roles_Change(Moderator.Session,
            chatId,
            null,
            null,
            Change.Create(new RoleDiff {
                Name = "escalated",
                Permissions = ChatPermissions.Owner,
                AuthorIds = new SetDiff<AuthorId[], AuthorId> { AddedItems = [moderatorAuthorId] },
            })));

        // assert
        await createRole.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ModeratorCannotInvite()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var outsider = fixture.AppHost.NewWebClientTester(Out);
        await using var _ = outsider.ConfigureAwait(false);
        var outsiderAccount = await outsider.SignInAsUniqueAlice();

        // act
        var invite = () => Moderator.Commander.Call(
            new Authors_Invite(Moderator.Session, chatId, [outsiderAccount.Id]));

        // assert
        await invite.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task PlaceModeratorCannotChangePlaceTypeOrDeletePlace()
    {
        // arrange
        var (placeId, _) = await ArrangePlace();

        // act
        var makePrivate = () => Moderator.Commander.Call(new Places_Change(Moderator.Session,
            placeId,
            null,
            Change.Update(new PlaceDiff { IsPublic = false })));
        var remove = () => Moderator.Commander.Call(
            new Places_Change(Moderator.Session, placeId, null, Change.Remove(new PlaceDiff())));

        // assert
        await makePrivate.Should().ThrowAsync<Exception>();
        await remove.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task PlaceModeratorCanEditPlaceTitle()
    {
        // arrange
        var (placeId, _) = await ArrangePlace();

        // act
        var updated = await Moderator.Commander.Call(new Places_Change(Moderator.Session,
            placeId,
            null,
            Change.Update(new PlaceDiff { Title = "moderated place title" })));

        // assert
        updated.Title.Should().Be("moderated place title");
    }

    [Fact]
    public async Task PromotingModeratorInvalidatesRules()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        var chats = Moderator.AppServices.GetRequiredService<IChats>();

        // act
        var promote = () => PromoteToModerator(moderatorAuthorId);

        // assert - Settle requires the value to arrive through invalidation, not a stale match
        var rules = await Settle(
            () => chats.GetRules(Moderator.Session, chatId, default),
            x => x.CanModerate(),
            promote);
        rules.CanEditProperties().Should().BeTrue();
    }

    [Fact]
    public async Task DemotingModeratorInvalidatesRulesAndModeratorList()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var chats = Moderator.AppServices.GetRequiredService<IChats>();
        var roles = Owner.AppServices.GetRequiredService<IRoles>();
        await Settle(
            () => chats.GetRules(Moderator.Session, chatId, default),
            x => x.CanModerate(),
            () => Task.CompletedTask,
            mustStartUnsettled: false);

        // act
        var demote = () => Owner.Commander.Call(
            new Authors_ChangeRole(Owner.Session, moderatorAuthorId, SystemRole.Moderator, false));

        // assert
        var moderatorIds = await Settle(
            () => roles.ListModeratorIds(Owner.Session, chatId, default),
            x => !x.Contains(moderatorAuthorId),
            demote);
        moderatorIds.Should().BeEmpty();

        var rules = await Settle(
            () => chats.GetRules(Moderator.Session, chatId, default),
            x => !x.CanModerate(),
            () => Task.CompletedTask,
            mustStartUnsettled: false);
        rules.CanEditProperties().Should().BeFalse();
    }

    [Fact]
    public async Task PlaceRoleChangeInvalidatesPublicPlaceChatRules()
    {
        // arrange - the appointment lands on the root chat, the watched rules on a child chat
        await Owner.SignInAsUniqueBob();
        var moderatorAccount = await Moderator.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(false, "invalidation place", moderatorAccount);
        var placeId = place.Id;
        await Moderator.JoinPlace(placeId);
        var moderatorAuthor = await Moderator.GetOwnAuthor(placeId.RootChatId).Require();
        var (publicChatId, _) = await Owner.CreateChat(true, "public place chat", placeId);
        var chats = Moderator.AppServices.GetRequiredService<IChats>();

        // act
        var appoint = () => Owner.Commander.Call(
            new Places_ChangeRole(Owner.Session, moderatorAuthor.Id, SystemRole.Moderator, true));

        // assert
        var rules = await Settle(
            () => chats.GetRules(Moderator.Session, publicChatId, default),
            x => x.CanModerate(),
            appoint);
        rules.CanEditProperties().Should().BeTrue();
    }

    // Private methods

    private async Task<(ChatId ChatId, AuthorId ModeratorAuthorId, AuthorId MemberAuthorId)> ArrangeChat()
    {
        await Owner.SignInAsUniqueBob();
        await Moderator.SignInAsUniqueAlice();
        await Member.SignInAsUniqueAlice();

        var (chatId, inviteId) = await Owner.CreateChat(false);
        var moderatorAuthor = await Moderator.JoinChat(chatId, inviteId);
        var memberAuthor = await Member.JoinChat(chatId, inviteId);
        return (chatId, moderatorAuthor.Id, memberAuthor.Id);
    }

    private async Task<(PlaceId PlaceId, AuthorId ModeratorAuthorId)> ArrangePlace()
    {
        await Owner.SignInAsUniqueBob();
        var moderatorAccount = await Moderator.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true, "moderated place", moderatorAccount);
        var placeId = place.Id;
        await Moderator.JoinPlace(placeId);
        var moderatorAuthor = await Moderator.GetOwnAuthor(placeId.RootChatId).Require();
        await Owner.Commander.Call(
            new Places_ChangeRole(Owner.Session, moderatorAuthor.Id, SystemRole.Moderator, true));
        await WaitForModerate(Moderator, placeId.RootChatId, true);
        return (placeId, moderatorAuthor.Id);
    }

    private Task PromoteToModerator(AuthorId authorId)
        => Owner.Commander.Call(new Authors_ChangeRole(Owner.Session, authorId, SystemRole.Moderator, true));

    private static async Task<AuthorRules> GetRules(WebClientTester tester, ChatId chatId)
    {
        var chats = tester.AppServices.GetRequiredService<IChats>();
        return await chats.GetRules(tester.Session, chatId, default);
    }

    private static async Task<bool> WaitForModerate(WebClientTester tester, ChatId chatId, bool isExpected)
    {
        // Rules are consolidated compute methods, and place chats resolve through the root chat,
        // so settle on the expected value rather than reading a stale one.
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var computed = await Computed.Capture(() => chats.GetRules(tester.Session, chatId, default));
        try {
            computed = await computed
                .When(x => x.CanModerate() == isExpected)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException) { }

        return computed.Value.CanModerate();
    }

    private static async Task<T> Settle<T>(
        Func<Task<T>> producer,
        Func<T, bool> isSettled,
        Func<Task> act,
        bool mustStartUnsettled = true)
    {
        var computed = await Computed.Capture(producer).ConfigureAwait(false);
        if (mustStartUnsettled)
            isSettled(computed.Value).Should().BeFalse("the invalidation chain must be what settles it");

        await act().ConfigureAwait(false);
        computed = await computed.When(isSettled).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        return computed.Value;
    }
}
