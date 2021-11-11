using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Chat.IntegrationTests;

public class ChatEntryReaderTest
{
    private const string ChatId = "the-actual-one";

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

        var reader = new ChatEntryReader(chats) {
            ChatId = ChatId,
            InvalidationWaitTimeout = TimeSpan.Zero,
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
    }
}
