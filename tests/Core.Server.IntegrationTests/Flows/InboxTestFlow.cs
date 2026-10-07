using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// Processes its inbox text by text. Texts it can't handle yet stay in the inbox: "keep:*" always,
// "block" until its gate opens. "fail-once" fails the first resume, "commit" commits mid-resume,
// "requeue:x" posts "x" to itself, "complete" completes the flow; an unreadable payload is "<poison>".

[Flow(ResumeTimeout = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class InboxTestFlow : Flow<Unit>, IInboxProcessingFlow
{
    public static ConcurrentDictionary<FlowId, (TaskCompletionSource Entered, TaskCompletionSource Release)> Gates
        { get; } = new();
    public static ConcurrentDictionary<FlowId, int> FailureCounts { get; } = new();

    [DataMember(Order = 0)]
    public List<string> Texts { get; set; } = new();
    [DataMember(Order = 1)]
    public int ResumeCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        ResumeCount++;
        foreach (var message in Inbox.Messages.ToList()) {
            var text = message.Payload is InboxTestPayload payload ? payload.Text : "<poison>";
            if (text.StartsWith("keep:"))
                continue;

            if (text == "block" && Gates.TryGetValue(Id, out var gate)) {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (text == "fail-once" && FailureCounts.AddOrUpdate(Id, 1, static (_, c) => c + 1) == 1)
                throw new TimeoutException("Fails once.");

            if (text.StartsWith("requeue:"))
                Inbox.Add(new InboxTestPayload(text["requeue:".Length..]));
            Texts.Add(text);
            Inbox.Remove(message);
            if (text == "commit")
                await Runtime.Commit(cancellationToken).ConfigureAwait(false);
            if (text == "complete") {
                SetResult(default);
                return;
            }
        }
    }
}

[DataContract, MessagePackObject]
public sealed partial record InboxTestPayload(
    [property: DataMember, Key(0)] string Text);
