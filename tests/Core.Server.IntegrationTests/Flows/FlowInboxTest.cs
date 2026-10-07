using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[CollectionDefinition(nameof(FlowInboxCollection))]
public sealed class FlowInboxCollection : ICollectionFixture<FlowInboxFixture>;

public sealed class FlowInboxFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(
    "flow-inbox",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.AddFlows(useMasterFlows: false)
            .Add<InboxTestFlow>()
            .Add<QuantaFlow>(),
    });

[Collection(nameof(FlowInboxCollection))]
public sealed class FlowInboxTest(FlowInboxFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<FlowInboxFixture>(fixture, @out)
{
    private IFlowBackend Backend => field ??= AppHost.Services.GetRequiredService<IFlowBackend>();

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await AppHost.Services.WhenFlowsStarted();
    }

    [Fact]
    public async Task PostedMessagesShouldReachTheFlowInOrder()
    {
        // arrange
        var flowId = NewFlowId();

        // act
        await Post(flowId, "a", "b", "c");
        await Post(flowId, "d");

        // assert
        await WhenTexts(flowId, "a", "b", "c", "d");
        await WhenInbox(flowId);
    }

    [Fact]
    public async Task MessageIdsShouldGrowAndNeverBeReused()
    {
        // arrange
        var flowId = NewFlowId();
        await Post(flowId, false, "a", "b");
        var first = await FlowHub.GetInboxNonComputed(flowId);
        await FlowHub.RemoveFromInbox(flowId, first.Select(m => m.Id));

        // act
        await Post(flowId, false, "c");

        // assert
        var second = await FlowHub.GetInboxNonComputed(flowId);
        first.Select(m => m.Id).Should().BeInAscendingOrder().And.OnlyContain(id => id > 0);
        second.Should().ContainSingle().Which.Id.Should().BeGreaterThan(first.Max(m => m.Id));
    }

    [Fact]
    public async Task UnprocessedMessagesShouldStayInTheInbox()
    {
        // arrange
        var flowId = NewFlowId();

        // act
        await Post(flowId, "keep:x", "y");

        // assert
        await WhenTexts(flowId, "y");
        await WhenInbox(flowId, "keep:x");
    }

    [Fact]
    public async Task MessagesPostedDuringAResumeShouldSurviveItsCommit()
    {
        // arrange
        var flowId = NewFlowId();
        var gate = (Entered: TaskCompletionSourceExt.New(), Release: TaskCompletionSourceExt.New());
        InboxTestFlow.Gates[flowId] = gate;
        await Post(flowId, "block");
        await TestWait.WhenPolled(() => gate.Entered.Task.IsCompleted.Should().BeTrue());

        // act
        await Post(flowId, false, "late");
        gate.Release.SetResult();

        // assert
        await WhenTexts(flowId, "block");
        await WhenInbox(flowId, "late");
        await FlowHub.NewResumeEvent(flowId).Schedule();
        await WhenTexts(flowId, "block", "late");
        await WhenInbox(flowId);
    }

    [Fact]
    public async Task CommitSkippedByAConcurrentStoreShouldRetryTheResume()
    {
        // arrange
        var flowId = NewFlowId();
        await Post(flowId, "keep:start");
        await WhenTexts(flowId);
        var gate = (Entered: TaskCompletionSourceExt.New(), Release: TaskCompletionSourceExt.New());
        InboxTestFlow.Gates[flowId] = gate;
        await Post(flowId, "block");
        await TestWait.WhenPolled(() => gate.Entered.Task.IsCompleted.Should().BeTrue());

        // act
        var storedFlow = await FlowHub.TryGet<InboxTestFlow>(flowId);
        await Commander.Call(new Flows_Store(flowId, storedFlow!.Version) { Flow = storedFlow });
        gate.Release.SetResult();

        // assert
        await WhenTexts(flowId, "block");
        await WhenInbox(flowId, "keep:start");
    }

    [Fact]
    public async Task FailedResumeShouldNeitherLoseNorRepeatMessages()
    {
        // arrange
        var flowId = NewFlowId();

        // act
        await Post(flowId, "a", "commit", "fail-once", "b");

        // assert
        await WhenTexts(flowId, "a", "commit", "fail-once", "b");
        await WhenInbox(flowId);
        InboxTestFlow.FailureCounts[flowId].Should().Be(2);
    }

    [Fact]
    public async Task FailedResumeShouldSaveNothingItDid()
    {
        // arrange
        var flowId = NewFlowId();

        // act
        await Post(flowId, "x", "requeue:y", "fail-once");

        // assert
        // Had the failed attempt stored its inbox changes, "x" would be gone before the retry read it
        // and "y" would be posted twice; had it stored its state, ResumeCount would be 2
        await WhenTexts(flowId, "x", "requeue:y", "fail-once");
        await WhenInbox(flowId, "y");
        var flow = await FlowHub.TryGet<InboxTestFlow>(flowId);
        flow!.ResumeCount.Should().Be(1);
        InboxTestFlow.FailureCounts[flowId].Should().Be(2);
    }

    [Fact]
    public async Task MessagesTheFlowPostsToItselfShouldBeStoredWithIt()
    {
        // arrange
        var flowId = NewFlowId();

        // act
        await Post(flowId, "requeue:x");

        // assert
        await WhenTexts(flowId, "requeue:x");
        await WhenInbox(flowId, "x");
        await FlowHub.NewResumeEvent(flowId).Schedule();
        await WhenTexts(flowId, "requeue:x", "x");
        await WhenInbox(flowId);
    }

    [Fact]
    public async Task UnreadablePayloadShouldStillBeRemovable()
    {
        // arrange
        var flowId = NewFlowId();
        var unreadable = new FlowInboxMessage(0, [0xC1, 0x00, 0xFF]);
        var postCommand = new Flows_ChangeInbox(flowId, new FlowInboxDiff([unreadable], [])) { MustResume = true };

        // act
        await Commander.Call(postCommand);

        // assert
        await WhenTexts(flowId, "<poison>");
        await WhenInbox(flowId);
    }

    [Fact]
    public async Task ConcurrentPostsShouldAllBeProcessedOnce()
    {
        // arrange
        var flowId = NewFlowId();
        var texts = Enumerable.Range(0, 30).Select(i => $"m{i}").ToArray();

        // act
        await texts.Select(text => Post(flowId, text)).Collect();

        // assert
        var flow = await TestWait.WhenPolled<InboxTestFlow>(async () => {
            var f = await FlowHub.TryGet<InboxTestFlow>(flowId);
            f.Should().NotBeNull();
            f.Texts.Should().HaveCount(texts.Length);
            return f;
        }, TimeSpan.FromSeconds(30));
        flow.Texts.Should().BeEquivalentTo(texts);
        await WhenInbox(flowId);
    }

    [Fact]
    public async Task AdditionsAndRemovalsShouldApplyTogether()
    {
        // arrange
        var flowId = NewFlowId();
        await Post(flowId, false, "a", "b");
        var posted = await FlowHub.GetInboxNonComputed(flowId);
        var added = FlowInboxMessage.New(new InboxTestPayload("c"));
        var changeCommand = new Flows_ChangeInbox(flowId, new FlowInboxDiff([added], [posted[0].Id]));

        // act
        await Commander.Call(changeCommand);

        // assert
        await WhenInbox(flowId, "b", "c");
    }

    [Fact]
    public async Task RemovingAbsentMessagesShouldChangeNothing()
    {
        // arrange
        var flowId = NewFlowId();
        await Post(flowId, false, "a");
        var before = await Backend.GetInbox(flowId, default);
        var cachedInbox = await Computed.Capture(() => Backend.GetInbox(flowId, default));

        // act
        await FlowHub.RemoveFromInbox(flowId, [before[0].Id + 1]);

        // assert
        cachedInbox.IsConsistent().Should().BeTrue();
        (await FlowHub.GetInboxNonComputed(flowId)).Should().Equal(before);
    }

    [Fact]
    public async Task InboxReadsShouldFollowPostsAndCommits()
    {
        // arrange
        var flowId = NewFlowId();
        var cachedInbox = await Computed.Capture(() => Backend.GetInbox(flowId, default));

        // act
        await Post(flowId, false, "a");

        // assert
        cachedInbox.IsConsistent().Should().BeFalse();
        (await Backend.GetInbox(flowId, default)).Should().HaveCount(1);
        await FlowHub.NewResumeEvent(flowId).Schedule();
        await TestWait.When(async ct => (await Backend.GetInbox(flowId, ct)).Should().BeEmpty());
    }

    [Fact]
    public async Task CompletedFlowShouldDropItsInboxAndIgnorePosts()
    {
        // arrange
        var flowId = NewFlowId();
        await Post(flowId, "keep:x", "complete");
        await WhenTexts(flowId, "complete");

        // act
        await Post(flowId, "late");

        // assert
        (await FlowHub.GetInboxNonComputed(flowId)).Should().BeEmpty();
    }

    [Fact]
    public async Task RemovedFlowShouldTakeItsInboxAlong()
    {
        // arrange
        var flowId = NewFlowId();
        await Post(flowId, "keep:x");
        var flow = await TestWait.WhenPolled<InboxTestFlow>(async () => {
            var f = await FlowHub.TryGet<InboxTestFlow>(flowId);
            f.Should().NotBeNull();
            f.ResumeCount.Should().Be(1);
            return f;
        });

        // act
        await Commander.Call(new Flows_Store(flowId, flow.Version));

        // assert
        (await FlowHub.GetInboxNonComputed(flowId)).Should().BeEmpty();
    }

    [Fact]
    public async Task PostingToAFlowWithoutAnInboxShouldFail()
    {
        // arrange
        var flowId = FlowHub.NewId<QuantaFlow>(RandomStringGenerator.Default.Next());
        var postCommand = Flows_ChangeInbox.Post(flowId, new InboxTestPayload("a"));

        // act, assert
        await FluentActions.Awaiting(() => Commander.Call(postCommand))
            .Should().ThrowAsync<InvalidOperationException>();
        (await FlowHub.GetInboxNonComputed(flowId)).Should().BeEmpty();
    }

    // Private methods

    private FlowId NewFlowId()
        => FlowHub.NewId<InboxTestFlow>(RandomStringGenerator.Default.Next());

    private Task Post(FlowId flowId, params string[] texts)
        => Post(flowId, true, texts);

    private Task Post(FlowId flowId, bool mustResume, params string[] texts)
        => FlowHub.PostToInbox(flowId, texts.Select(t => (object)new InboxTestPayload(t)), mustResume);

    private Task WhenTexts(FlowId flowId, params string[] texts)
        => TestWait.WhenPolled(async () => {
            var flow = await FlowHub.TryGet<InboxTestFlow>(flowId);
            flow.Should().NotBeNull();
            flow.Texts.Should().Equal(texts);
        });

    private Task WhenInbox(FlowId flowId, params string[] texts)
        => TestWait.WhenPolled(async () => {
            var messages = await FlowHub.GetInboxNonComputed(flowId);
            messages.Select(m => ((InboxTestPayload)m.Payload!).Text).Should().Equal(texts);
        });
}
