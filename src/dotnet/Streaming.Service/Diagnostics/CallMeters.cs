using System.Diagnostics.Metrics;
using ActualChat.Live;

namespace ActualChat.Streaming.Diagnostics;

/// <summary>
/// Counts calls as <see cref="LiveSessionsBackend"/> places and ends them. Every tag is an enum name or a
/// fixed word, never an id: per-call detail is the log line next to each record.
/// </summary>
public static class CallMeters
{
    public static readonly Counter<long> Started;
    public static readonly Counter<long> StartRefused;
    public static readonly Counter<long> AcceptRefused;
    public static readonly Counter<long> Ended;
    public static readonly Counter<long> Invites;
    public static readonly Histogram<double> RingDeliveryDelay;
    public static readonly Histogram<double> AnswerDelay;
    public static readonly Histogram<double> DialDuration;
    public static readonly Histogram<double> Duration;
    public static readonly Histogram<int> PartyCount;

    // A ring lasts RingTimeout (20 s) plus AnswerGrace (10 s): finer than the OTel defaults inside that.
    private static readonly double[] RingBucketBoundsSec = [0.5, 1, 2, 3, 5, 7, 10, 15, 20, 30, 45, 60];
    // Talk time: the first buckets separate a call that failed right after connecting from one that was had.
    private static readonly double[] DurationBucketBoundsSec = [5, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200];
    private static readonly int[] PartyCountBucketBounds = [1, 2, 3, 4, 6, 8, 12, 16];

    static CallMeters()
    {
        var m = StreamingInstruments.Meter;
        Started = m.CreateCounter<long>(
            "calls.started", null,
            "Calls placed or joined; tags: chat_kind, has_video, kind (placed | joined)");
        StartRefused = m.CreateCounter<long>(
            "calls.start_refused", null,
            "StartCall requests refused; tags: reason (caller_busy | chat_busy | just_ended)");
        AcceptRefused = m.CreateCounter<long>(
            "calls.accept_refused", null,
            "AcceptCall requests refused: the ring was over or another call's");
        Ended = m.CreateCounter<long>(
            "calls.ended", null,
            "Calls ended, answered or not; tags: outcome, reason, chat_kind, has_video");
        Invites = m.CreateCounter<long>(
            "calls.invites", null,
            "Invites of ended calls by their final status and the ring ack their client sent; tags: status, ack");
        RingDeliveryDelay = m.CreateHistogram(
            "calls.ring_delivery_delay", "s",
            "From the ring's write to the invitee's client confirming it rang",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = RingBucketBoundsSec });
        AnswerDelay = m.CreateHistogram(
            "calls.answer_delay", "s", "From StartCall to the first answer",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = RingBucketBoundsSec });
        DialDuration = m.CreateHistogram(
            "calls.dial_duration", "s", "How long an unanswered call dialed before it ended; tags: outcome",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = RingBucketBoundsSec });
        Duration = m.CreateHistogram(
            "calls.duration", "s", "Talk time of an answered call; tags: chat_kind, has_video",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBucketBoundsSec });
        PartyCount = m.CreateHistogram(
            "calls.party_count", null, "Parties of an answered call at its end; tags: chat_kind",
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = PartyCountBucketBounds });
    }

    public static void RecordStarted(ChatId chatId, bool hasVideo, bool isJoining)
        => Started.Add(1, new TagList {
            { "chat_kind", chatId.Kind.ToString() },
            { "has_video", hasVideo },
            { "kind", isJoining ? "joined" : "placed" },
        });

    public static void RecordStartRefused(string reason)
        => StartRefused.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordEnded(
        LiveCall call,
        CallOutcome outcome,
        CallEndReason reason,
        Moment endedAt,
        IReadOnlyCollection<CallInvite> invites,
        int partyCount)
    {
        var chatKind = call.ChatId.Kind.ToString();
        Ended.Add(1, new TagList {
            { "outcome", outcome.ToString() },
            { "reason", reason.ToString() },
            { "chat_kind", chatKind },
            { "has_video", call.HasVideo },
        });
        foreach (var invite in invites) {
            Invites.Add(1, new TagList {
                { "status", invite.Status.ToString() },
                { "ack", invite.Ack?.ToString() ?? "None" },
            });
            if (invite is { Ack: RingAck.Received or RingAck.Ringing, AckAt: { } ackAt })
                RingDeliveryDelay.Record((ackAt - invite.RingingAt).TotalSeconds);
        }
        if (call.AnsweredAt is { } answeredAt) {
            AnswerDelay.Record((answeredAt - call.StartedAt).TotalSeconds);
            Duration.Record((endedAt - answeredAt).TotalSeconds, new TagList {
                { "chat_kind", chatKind },
                { "has_video", call.HasVideo },
            });
            PartyCount.Record(partyCount, new KeyValuePair<string, object?>("chat_kind", chatKind));
        }
        else
            DialDuration.Record((endedAt - call.StartedAt).TotalSeconds,
                new KeyValuePair<string, object?>("outcome", outcome.ToString()));
    }
}
