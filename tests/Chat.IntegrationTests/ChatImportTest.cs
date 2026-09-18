using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

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
        var date = new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var firstResult = await Import(first, import.Id, [new(owner.Id, date, "first chat")]);
        var secondResult = await Import(second, import.Id, [new(owner.Id, date, "second chat")]);

        // assert
        firstResult[0].Error.IsNone.Should().BeTrue();
        secondResult[0].Error.IsNone.Should().BeTrue();
        (await Imports.Get(Owner.Session, second, default))!.Id.Should().Be(import.Id);
        (await Imports.HasConsent(Owner.Session, second, import.Id, default)).Should().BeTrue();
        (await Owner.Chats.Get(Owner.Session, first, default))!.Rules.CanWrite().Should().BeFalse();
        (await Owner.Chats.Get(Owner.Session, second, default))!.Rules.CanWrite().Should().BeFalse();
        await FluentActions.Awaiting(() => Start(first)).Should().ThrowAsync<Exception>();
        await End(place.Id.RootChatId, import.Id);
    }

    [Fact]
    public async Task RetriesShouldReturnOriginalResultsAndRejectChangedPayloads()
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await ClearHistory(chatId);
        var import = await Start(chatId);
        await Consent(Owner, chatId, import.Id, true);
        var command = new ChatImports_ImportEntries {
            Session = Owner.Session, ChatId = chatId, ImportId = import.Id,
            Entries = [new(owner.Id, new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), "once")],
        };

        // act
        var first = await Owner.Commander.Call(command);
        var second = await Owner.Commander.Call(command);

        // assert
        first[0].Error.IsNone.Should().BeTrue();
        second.Should().BeEquivalentTo(first);
        await FluentActions.Awaiting(() => Owner.Commander.Call(command with {
            Entries = [command.Entries[0] with { Content = "different" }],
        })).Should().ThrowAsync<Exception>();
        await End(chatId, import.Id);
    }

    [Fact]
    public async Task ConsentShouldBeRevocableAndSpecificToTheSession()
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        await Member.SignInAsUniqueBob();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        await Member.JoinChat(chatId, inviteId);
        await ClearHistory(chatId);
        var import = await Start(chatId);
        await Consent(Owner, chatId, import.Id, true);
        var entries = new[] { new ChatImportEntry(owner.Id,
            new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), "historical") }.ToApiArray();

        // act
        await Consent(Owner, chatId, import.Id, false);
        var revoked = await Import(chatId, import.Id, entries);
        await End(chatId, import.Id);
        var next = await Start(chatId);
        var newSession = await Import(chatId, next.Id, entries);

        // assert
        revoked[0].Error.IsNone.Should().BeFalse();
        newSession[0].Error.IsNone.Should().BeFalse();
        await FluentActions.Awaiting(() => Consent(Owner, chatId, import.Id, true)).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Member.Commander.Call(new ChatImports_End {
            Session = Member.Session, ChatId = chatId, ImportId = next.Id,
        })).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Member.Commander.Call(new ChatImports_ImportEntries {
            Session = Member.Session, ChatId = chatId, ImportId = next.Id, Entries = entries,
        })).Should().ThrowAsync<Exception>();
        await End(chatId, next.Id);
    }

    [Fact]
    public async Task ConcurrentBatchesShouldSerializeAgainstTheCommittedTail()
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await ClearHistory(chatId);
        var import = await Start(chatId);
        await Consent(Owner, chatId, import.Id, true);
        var date = new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // act
        var batches = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(i => Import(chatId, import.Id, [new(owner.Id, date, $"batch {i}")])));

        // assert
        batches.Count(x => x[0].Error.IsNone).Should().Be(1);
        batches.Count(x => !x[0].Error.IsNone).Should().Be(3);
        await End(chatId, import.Id);
    }

    [Fact]
    public async Task ImportShouldUseTheExistingVisibleTail()
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await ClearHistory(chatId);
        var existing = await Owner.CreateTextEntry(chatId, "existing");
        var entry = await Owner.Chats.GetEntry(Owner.Session, existing.Id, default);
        var import = await Start(chatId);
        await Consent(Owner, chatId, import.Id, true);

        // act
        var result = await Import(chatId, import.Id, [
            new(owner.Id, entry!.BeginsAt - TimeSpan.FromSeconds(1), "older"),
            new(owner.Id, entry.BeginsAt, "equal"),
            new(owner.Id, entry.BeginsAt + TimeSpan.FromSeconds(1), "newer"),
        ]);

        // assert
        result[0].Error.IsNone.Should().BeFalse();
        result[1].Error.IsNone.Should().BeFalse();
        result[2].Error.IsNone.Should().BeTrue();
        await End(chatId, import.Id);
    }

    [Fact]
    public async Task UploadShouldBeAttributedAndBoundToOneConsentedMessage()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var member = await Member.SignInAsUniqueBob();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        await Member.JoinChat(chatId, inviteId);
        await ClearHistory(chatId);
        var import = await Start(chatId);
        await Consent(Member, chatId, import.Id, true);
        var data = "imported attachment"u8.ToArray();
        var uploadId = await Owner.Commander.Call(new ChatImports_CreateUpload {
            Session = Owner.Session, ChatId = chatId, ImportId = import.Id, UserId = member.Id,
            Length = data.Length, FileName = "history.txt", ContentType = "text/plain",
        });
        var append = new Uploads_Append { Session = Owner.Session, UploadId = uploadId, Offset = 0, Chunk = data };

        // act
        await Consent(Member, chatId, import.Id, false);
        await FluentActions.Awaiting(() => Owner.Commander.Call(append)).Should().ThrowAsync<Exception>();
        await Consent(Member, chatId, import.Id, true);
        await Owner.Commander.Call(append);
        var media = await Owner.Commander.Call(new ChatImports_FinalizeUpload {
            Session = Owner.Session, ChatId = chatId, ImportId = import.Id, UploadId = uploadId,
        });
        var date = new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var result = await Import(chatId, import.Id, [
            new(member.Id, date, "attachment") { UploadIds = [uploadId] },
            new(member.Id, date + TimeSpan.FromSeconds(1), "reuse") { UploadIds = [uploadId] },
        ]);

        // assert
        result[0].Error.IsNone.Should().BeTrue();
        result[1].Error.IsNone.Should().BeFalse();
        var full = await Owner.AppServices.GetRequiredService<IMediaBackend>().GetFull(media.MediaId, default);
        full!.UserId.Should().Be(member.Id);
        var attachments = await Owner.AppServices.GetRequiredService<IChatsBackend>()
            .GetEntryAttachments(result[0].EntryId!, default);
        attachments.Should().ContainSingle(x => x.MediaId == media.MediaId);
        await End(chatId, import.Id);
    }

    [Fact]
    public async Task PersistenceFailureShouldRollBackEntriesAndBatchReceipt()
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await ClearHistory(chatId);
        var import = await Start(chatId);
        await Consent(Owner, chatId, import.Id, true);
        var backend = Owner.AppServices.GetRequiredService<IChatsBackend>();
        var before = await backend.GetMaxLid(chatId, true, default);
        var date = new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var command = new ChatImports_ImportEntries {
            Session = Owner.Session, ChatId = chatId, ImportId = import.Id,
            Entries = [
                new(owner.Id, date, "first"),
                new(owner.Id, date + TimeSpan.FromSeconds(1), "chat-import-persistence-failure"),
            ],
        };
        await using var db = await Owner.AppServices.DbHub<ChatDbContext>().CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE chat_entries ADD CONSTRAINT chat_import_atomicity_test
            CHECK (content <> 'chat-import-persistence-failure') NOT VALID
            """);

        // act
        try {
            await FluentActions.Awaiting(() => Owner.Commander.Call(command)).Should().ThrowAsync<Exception>();

            // assert
            (await backend.GetMaxLid(chatId, true, default)).Should().Be(before);
            (await db.ChatEntries.CountAsync(x => x.ChatId == chatId.Value && x.IsImported)).Should().Be(0);
            (await db.ChatImportBatches.AnyAsync(x => x.Id.StartsWith(import.Id + ":"))).Should().BeFalse();
        }
        finally {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE chat_entries DROP CONSTRAINT chat_import_atomicity_test");
        }
        var retry = await Owner.Commander.Call(command);
        retry.Should().OnlyContain(x => x.Error.IsNone);
        await End(chatId, import.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchShouldSerializeWithRevocationAndEnd(bool revoke)
    {
        // arrange
        var owner = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await ClearHistory(chatId);
        var import = await Start(chatId);
        await Consent(Owner, chatId, import.Id, true);
        var date = new Moment(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // act
        var importing = TryImport();
        var stopping = revoke ? Consent(Owner, chatId, import.Id, false) : End(chatId, import.Id);
        await Task.WhenAll(importing, stopping);

        // assert
        await using var db = await Owner.AppServices.DbHub<ChatDbContext>().CreateDbContext();
        var count = await db.ChatEntries.CountAsync(x => x.ChatId == chatId.Value && x.IsImported);
        count.Should().BeOneOf(0, 2);
        if (revoke) {
            var later = await Import(chatId, import.Id, [new(owner.Id, date + TimeSpan.FromSeconds(2), "too late")]);
            later[0].Error.IsNone.Should().BeFalse();
            await End(chatId, import.Id);
        }
        else
            await FluentActions.Awaiting(() => Import(chatId, import.Id,
                [new(owner.Id, date + TimeSpan.FromSeconds(2), "too late")])).Should().ThrowAsync<Exception>();
        return;

        async Task TryImport() {
            try {
                await Import(chatId, import.Id, [
                    new(owner.Id, date, "first"),
                    new(owner.Id, date + TimeSpan.FromSeconds(1), "second"),
                ]);
            }
            catch (Exception) when (!revoke) { }
        }
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
