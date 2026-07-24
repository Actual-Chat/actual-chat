namespace ActualChat.Notifications.IntegrationTests;

public class PushMessageTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void JsonRoundtrips()
    {
        var author = AuthorId.New(TestChatId, 1);
        var sentAt = Moment.Now;
        var messages = new[] {
            NotificationMessage.New(author, "Alice", "first", 100, sentAt),
            NotificationMessage.New(author, "Bob", "second", 101, sentAt + TimeSpan.FromSeconds(1)),
        }.ToApiArray();

        var parsed = PushMessage.FromJson(PushMessage.ToJson(messages));

        parsed.Should().HaveCount(2);
        parsed[0].AuthorName.Should().Be("Alice");
        parsed[0].Text.Should().Be("first");
        parsed[1].AuthorName.Should().Be("Bob");
        parsed[1].SentAtMs.Should().Be((long)(sentAt + TimeSpan.FromSeconds(1)).EpochOffset.TotalMilliseconds);
    }

    [Fact]
    public void ToJsonDropsOldestOverBudget()
    {
        var author = AuthorId.New(TestChatId, 1);
        // NotificationMessage.New truncates, so build oversized entries via the record directly.
        var messages = Enumerable.Range(0, 5)
            .Select(i => new NotificationMessage {
                AuthorId = author,
                AuthorName = $"author{i}",
                Text = new string((char)('a' + i), 700),
                EntryLid = 100 + i,
                SentAt = Moment.Now,
            })
            .ToApiArray();

        var json = PushMessage.ToJson(messages);

        json.Length.Should().BeLessThanOrEqualTo(PushMessage.MaxJsonLength + 800);
        var parsed = PushMessage.FromJson(json);
        parsed.Should().HaveCountLessThan(5);
        parsed[^1].Text.Should().StartWith("e"); // the newest message always survives
    }

    [Fact]
    public void ToJsonRespectsExplicitBudget()
    {
        var author = AuthorId.New(TestChatId, 1);
        var messages = Enumerable.Range(0, 3)
            .Select(i => NotificationMessage.New(
                author, $"a{i}", new string((char)('a' + i), 100), 100 + i, Moment.Now))
            .ToApiArray();

        var json = PushMessage.ToJson(messages, 300);

        var parsed = PushMessage.FromJson(json);
        parsed.Should().HaveCountLessThan(3);
        parsed[^1].Text.Should().StartWith("c");
    }

    [Fact]
    public void ToJsonReturnsEmptyWhenNewestCannotFit()
    {
        var author = AuthorId.New(TestChatId, 1);
        var messages = new[] {
            NotificationMessage.New(author, "Alice", new string('x', 150), 100, Moment.Now),
        }.ToApiArray();

        PushMessage.ToJson(messages, 50).Should().BeEmpty();
    }

    [Fact]
    public void FromJsonToleratesGarbage()
    {
        PushMessage.FromJson(null).Should().BeEmpty();
        PushMessage.FromJson("").Should().BeEmpty();
        PushMessage.FromJson("not json").Should().BeEmpty();
    }
}
