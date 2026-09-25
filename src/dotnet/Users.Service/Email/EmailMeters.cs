using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;

namespace ActualChat.Users.Email;

public static class EmailMeters
{
    public static readonly Counter<long> DigestRuns;
    public static readonly Counter<long> DigestSends;
    public static readonly Counter<long> DigestSubscriptions;

    static EmailMeters()
    {
        var m = CoreServerInstruments.Meter;
        DigestRuns = m.CreateCounter<long>(
            "emails.digest.runs", null, "Digest flow runs, by result");
        DigestSends = m.CreateCounter<long>(
            "emails.digest.sends", null, "Digest send attempts, by outcome");
        DigestSubscriptions = m.CreateCounter<long>(
            "emails.digest.subscriptions", null, "Digest turned on or off, by action and source");
    }

    public static void RecordRun(string result)
        => DigestRuns.Add(1, new KeyValuePair<string, object?>("result", result));

    public static void RecordSend(string outcome)
        => DigestSends.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordSubscription(bool isEnabled, string source)
        => DigestSubscriptions.Add(1,
            new KeyValuePair<string, object?>("action", isEnabled ? "on" : "off"),
            new KeyValuePair<string, object?>("source", source));
}
