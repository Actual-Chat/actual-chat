using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ChatImportTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Member => field ??= fixture.AppHost.NewWebClientTester(Out);
    private IChatImports Imports => Owner.AppServices.GetRequiredService<IChatImports>();

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await Member.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ImportShouldRequireConsentAndBlockOrdinaryOwnerWrites()
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        var member = await Member.SignInAsUniqueBob();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        await Member.JoinChat(chatId, inviteId);
        await ClearHistory(chatId);
        var import = await Start(chatId);
        var date = new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // act
        var rejected = await Import(chatId, import.Id, [new(member.Id, date, "without consent")]);
        await Consent(Member, chatId, import.Id, true);
        var accepted = await Import(chatId, import.Id, [new(member.Id, date, "with consent")]);

        // assert
        rejected[0].Error.IsNone.Should().BeFalse();
        accepted[0].Error.IsNone.Should().BeTrue();
        var entry = await Owner.Chats.GetEntry(Owner.Session, accepted[0].EntryId!, default);
        entry!.BeginsAt.Should().Be(date);
        entry.AuthorId.Should().Be((await Member.Authors.GetOwn(Member.Session, chatId, default))!.Id);
        entry.IsImported.Should().BeTrue();
        await FluentActions.Awaiting(() => Owner.CreateTextEntry(chatId, "blocked")).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Owner.RemoveTextEntry(entry.Id)).Should().ThrowAsync<Exception>();
        var summary = await Imports.GetConsentSummary(Owner.Session, chatId, 0, 5, default);
        summary.ConsentingCount.Should().Be(1);
        summary.NonConsentingUserIds.Should().Contain(owner.Id);
        await End(chatId, import.Id);
        await Owner.CreateTextEntry(chatId, "after import");
    }

    [Fact]
    public async Task BatchShouldSortAndReturnErrorsInInputOrder()
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await ClearHistory(chatId);
        var import = await Start(chatId);
        await Consent(Owner, chatId, import.Id, true);
        var date = new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // act
        var result = await Import(chatId, import.Id, [
            new(owner.Id, date + TimeSpan.FromSeconds(2), "third"),
            new(owner.Id, date, "first"),
            new(owner.Id, date + TimeSpan.FromSeconds(1), "second"),
            new(owner.Id, date, "duplicate"),
        ]);
        var next = await Import(chatId, import.Id, [
            new(owner.Id, date + TimeSpan.FromSeconds(1), "too old"),
            new(owner.Id, date + TimeSpan.FromSeconds(3), "fourth"),
        ]);

        // assert
        result.Take(3).Should().OnlyContain(x => x.Error.IsNone);
        result[3].Error.IsNone.Should().BeFalse();
        result[1].EntryId!.LocalId.Should().BeLessThan(result[2].EntryId!.LocalId);
        result[2].EntryId!.LocalId.Should().BeLessThan(result[0].EntryId!.LocalId);
        next[0].Error.IsNone.Should().BeFalse();
        next[1].Error.IsNone.Should().BeTrue();
        await End(chatId, import.Id);
    }

    [Fact]
    public async Task PlaceImportShouldShareConsentAndMaintenance()
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        var (first, _) = await Owner.CreateChat(true, placeId: place.Id);
        var (second, _) = await Owner.CreateChat(true, placeId: place.Id);
        var import = await Start(place.Id.RootChatId);

        // act
        await Consent(Owner, first, import.Id, true);

        // assert
        (await Imports.Get(Owner.Session, second, default))!.Id.Should().Be(import.Id);
        (await Imports.HasConsent(Owner.Session, second, import.Id, default)).Should().BeTrue();
        (await Owner.Chats.Get(Owner.Session, first, default))!.Rules.CanWrite().Should().BeFalse();
        (await Owner.Chats.Get(Owner.Session, second, default))!.Rules.CanWrite().Should().BeFalse();
        await FluentActions.Awaiting(() => Start(first)).Should().ThrowAsync<Exception>();
        await End(place.Id.RootChatId, import.Id);
    }

    // Private methods

    private async Task ClearHistory(ChatId chatId)
    {
        var backend = Owner.AppServices.GetRequiredService<IChatsBackend>();
        var memberCount = (await Owner.Authors.ListAuthorIds(Owner.Session, chatId, default)).Length;
        await ComputedTest.When(async ct => {
            (await backend.GetMaxLid(chatId, true, ct)).Should().BeGreaterThanOrEqualTo(memberCount);
        }, TimeSpan.FromSeconds(5));
        var tail = await backend.GetMaxLid(chatId, true, default);
        for (var localId = 1L; localId <= tail; localId++) {
            var id = ChatEntryId.New(chatId, localId);
            if (await Owner.Chats.GetEntry(Owner.Session, id, default) is { IsRemoved: false })
                await Owner.Commander.Call(new ChatsBackend_ChangeEntry(id, null, Change.Remove<ChatEntryDiff>()));
        }
    }

    private Task<ChatImportSession> Start(ChatId chatId)
        => Owner.Commander.Call(new ChatImports_Start { Session = Owner.Session, ChatId = chatId });

    private Task End(ChatId chatId, string importId)
        => Owner.Commander.Call(new ChatImports_End {
            Session = Owner.Session, ChatId = chatId, ImportId = importId,
        });

    private static Task Consent(WebClientTester tester, ChatId chatId, string importId, bool hasConsent)
        => tester.Commander.Call(new ChatImports_SetConsent {
            Session = tester.Session, ChatId = chatId, ImportId = importId, HasConsent = hasConsent,
        });

    private Task<ApiArray<ChatImportEntryResult>> Import(
        ChatId chatId, string importId, ApiArray<ChatImportEntry> entries)
        => Owner.Commander.Call(new ChatImports_ImportEntries {
            Session = Owner.Session, ChatId = chatId, ImportId = importId, Entries = entries,
        });
}
