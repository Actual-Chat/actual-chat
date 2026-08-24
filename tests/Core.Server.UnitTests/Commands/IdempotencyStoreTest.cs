namespace ActualChat.Commands.UnitTests;

public sealed class IdempotencyStoreTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly byte[] Result = [1, 2, 3];

    [Fact]
    public void SecondCallerGetsTheClaimInsteadOfOwningIt()
    {
        // arrange
        var store = new IdempotencyStore();

        // act
        var isClaimed = store.TryClaim("k", out var claim);
        var isReclaimed = store.TryClaim("k", out var duplicate);

        // assert
        isClaimed.Should().BeTrue();
        isReclaimed.Should().BeFalse();
        duplicate.Should().BeSameAs(claim);
        duplicate.Result.Should().BeNull();
    }

    [Fact]
    public void CompletedResultIsVisibleToDuplicates()
    {
        // arrange
        var store = new IdempotencyStore();
        store.TryClaim("k", out var claim);

        // act
        claim.Complete(Result);
        var isClaimed = store.TryClaim("k", out var duplicate);

        // assert
        isClaimed.Should().BeFalse();
        duplicate.Result!.Value.ToArray().Should().Equal(Result);
    }

    [Fact]
    public async Task WaiterGetsTheResultTheOwnerCompletesWith()
    {
        // arrange
        var store = new IdempotencyStore();
        store.TryClaim("k", out var claim);
        store.TryClaim("k", out var duplicate);

        // act
        var whenCompleted = duplicate.WhenCompleted;
        claim.Complete(Result);

        // assert
        var result = await whenCompleted.WaitAsync(TimeSpan.FromSeconds(1));
        result!.Value.ToArray().Should().Equal(Result);
    }

    [Fact]
    public async Task ReleaseWakesWaitersWithNoResultAndFreesTheKey()
    {
        // arrange
        var store = new IdempotencyStore();
        store.TryClaim("k", out var claim);
        store.TryClaim("k", out var duplicate);

        // act
        var whenCompleted = duplicate.WhenCompleted;
        claim.Release();

        // assert
        var result = await whenCompleted.WaitAsync(TimeSpan.FromSeconds(1));
        result.Should().BeNull();
        store.TryClaim("k", out _).Should().BeTrue();
        store.EntryCount.Should().Be(1);
    }

    [Fact]
    public async Task ExpiredClaimIsTakenOverByTheNextCaller()
    {
        // arrange
        var store = new IdempotencyStore { InProgressTtl = TimeSpan.FromMilliseconds(50) };
        store.TryClaim("k", out _);

        // act
        await Task.Delay(200);

        // assert
        store.TryClaim("k", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CompletedResultStopsBeingReplayedAfterCompletedTtl()
    {
        // arrange
        var store = new IdempotencyStore { CompletedTtl = TimeSpan.FromMilliseconds(50) };
        store.TryClaim("k", out var claim);
        claim.Complete(Result);

        // act
        await Task.Delay(200);

        // assert
        store.TryClaim("k", out var nextClaim).Should().BeTrue();
        nextClaim.Result.Should().BeNull();
    }

    [Fact]
    public void PruneKeepsEntryCountUnderTheCap()
    {
        // arrange
        var store = new IdempotencyStore { MaxEntryCount = 5, PruneInterval = TimeSpan.Zero };

        // act
        for (var i = 0; i < 50; i++) {
            store.TryClaim($"k{i}", out var claim);
            claim.Complete(Result);
        }

        // assert
        store.EntryCount.Should().BeLessThanOrEqualTo(6);
    }
}
