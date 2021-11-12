using ActualChat.Chat.Db;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.EntityFramework;
using Stl.Time.Testing;
using Stl.Versioning;
using Stl.Versioning.Providers;

namespace ActualChat.Chat.IntegrationTests;

public class ChatEntryReaderTest
{
    private readonly VersionGenerator<long> VersionGenerator = new ClockBasedVersionGenerator(SystemClock.Instance);
    private const string ChatId = "the-actual-one";
    private readonly MomentClockSet Clocks = new MomentClockSet();

    [Fact]
    public async Task ReaderTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var session = tester.Session;

        var chats = tester.ClientServices.GetRequiredService<IChats>();
        var idRange = await chats.GetIdRange(session, ChatId, CancellationToken.None);
        idRange.Start.Should().Be(131);
        idRange.End.Should().Be(142);

        var chat = await chats.Get(session, ChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");
        chat?.CreatedAt.Date.Should().Be(DateTime.Now.Date);

        var dbContextFactory = tester.AppServices.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync();

        var dbChat = await dbContext.Chats.FirstOrDefaultAsync(c => c.Id == ChatId);

        var dbAuthor = await dbContext.ChatAuthors.FirstOrDefaultAsync(CancellationToken.None);
        await AddTextMessages(dbContext, dbChat, dbAuthor, CancellationToken.None);

        var reader = new ChatEntryReader(chats) {
            ChatId = ChatId,
            InvalidationWaitTimeout = TimeSpan.FromSeconds(1),
            Session = session,
        };

        var entry = await reader.Get(131, CancellationToken.None);
        entry.Should().NotBeNull();
        entry?.Content.Should().Be("audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm");
        entry?.BeginsAt.Should().Be(new Moment(DateTime.Parse("2021-11-05T16:41:18.5043140Z")));
        entry?.Duration.Should().Be(11.039);

        entry = await reader.Get(132, CancellationToken.None);
        entry.Should().NotBeNull();
        entry?.Content.Should().Be("Мой друг художник и поэт в Дождливый вечер на стекле мою любовь нарисовал открыв мне чудо на Земле");
        entry?.EndsAt.Should().Be(new Moment(DateTime.Parse("2021-11-05T16:41:29.0043140Z")));
        entry?.Duration.Should().Be(10.5);

        var entryPoint = new Moment(DateTime.Parse("2021-11-05T16:41:30.0043140Z"));

        var nextEntryId = await reader.GetNextEntryId(entryPoint, CancellationToken.None);
        nextEntryId.Should().Be(133);

        entry = await reader.Get(145, CancellationToken.None);
        entry.Should().NotBeNull();
        entry?.Content.Should().Be("back in black i hit the sack");

        entry = await reader.Get(146, CancellationToken.None);
        entry.Should().NotBeNull();
        entry?.Content.Should().Be("rape me rape me my friend");

        entry = await reader.Get(147, CancellationToken.None);
        entry.Should().NotBeNull();
        entry?.Content.Should().Be("it was a teenage wedding and the all folks wished them well");
    }

    private async Task AddTextMessages(
        DbContext dbContext,
        DbChat dbChat,
        DbChatAuthor dbAuthor,
        CancellationToken cancellationToken)
    {
        var phrases = new[] {
            "back in black i hit the sack",
            "rape me rape me my friend",
            "it was a teenage wedding and the all folks wished them well",
        };
        var id = 145;
        foreach (var text in phrases)
        {
            var dbChatEntry = new DbChatEntry() {
                CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, id),
                ChatId = dbChat.Id,
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = Clocks.SystemClock.Now,
                EndsAt = Clocks.SystemClock.Now,
                Type = ChatEntryType.Text,
                Content = text,
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(dbChatEntry);
            id++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
