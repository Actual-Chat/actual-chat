using System.Text;

namespace ActualChat.Notifications.IntegrationTests;

public class DismissalPayloadChunkTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const int MaxPayloadBytes = 4096 - 512;
    private static readonly UserId TestUserId = UserId.New();
    private static readonly Moment QueuedAt = Moment.EpochStart;

    [Fact]
    public void ShouldKeepEveryChunkUnderTheFcmPayloadLimit()
    {
        // arrange
        var dismissals = ManyDismissals();

        // act
        var chunks = FirebaseMessagingClient.ChunkDismissals(dismissals);

        // assert
        chunks.Count.Should().BeGreaterThan(1, "256 dismissals don't fit a single 4KB FCM payload");
        foreach (var chunk in chunks) {
            var ids = string.Join(',', chunk.Dismissals.Select(x => x.Id.Value));
            var payload = Encoding.UTF8.GetByteCount(ids)
                + Encoding.UTF8.GetByteCount(string.Join(',', chunk.Tags));
            payload.Should().BeLessThan(MaxPayloadBytes, "both keys ride in the same payload");
        }
    }

    [Fact]
    public void ShouldChunkWithoutLosingOrDuplicatingADismissal()
    {
        // arrange
        var dismissals = ManyDismissals();

        // act
        var chunks = FirebaseMessagingClient.ChunkDismissals(dismissals);

        // assert
        chunks.SelectMany(c => c.Dismissals).Should().BeEquivalentTo(dismissals);
    }

    [Fact]
    public void ShouldSendOneChunkWhenEveryTagFits()
    {
        // arrange
        var dismissals = new[] { NewDismissal("s-a-b"), NewDismissal("s-c-d") };

        // act
        var chunks = FirebaseMessagingClient.ChunkDismissals(dismissals);

        // assert
        chunks.Should().ContainSingle().Which.Tags.Should().BeEquivalentTo("s-a-b", "s-c-d");
    }

    [Fact]
    public void ShouldCarryASharedTagOnlyOnce()
    {
        // arrange
        var dismissals = new[] { NewDismissal("s-a-b"), NewDismissal("s-a-b"), NewDismissal("") };

        // act
        var chunks = FirebaseMessagingClient.ChunkDismissals(dismissals);

        // assert
        var chunk = chunks.Should().ContainSingle().Subject;
        chunk.Tags.Should().ContainSingle().Which.Should().Be("s-a-b");
        chunk.Dismissals.Should().HaveCount(3, "an untagged dismissal still refreshes the badge");
    }

    [Fact]
    public void ShouldProduceNoChunksForNoDismissals()
        => FirebaseMessagingClient.ChunkDismissals([]).Should().BeEmpty();

    // Private methods

    private static List<PendingDismissal> ManyDismissals()
        => Enumerable
            .Range(0, Constants.Notification.MaxPendingDismissals)
            .Select(i => NewDismissal($"s-{i:D19}-{i:D19}"))
            .ToList();

    private static PendingDismissal NewDismissal(string tag)
        => new(NotificationId.New(TestUserId, NotificationKind.Message, "key-" + tag), tag, QueuedAt);
}
