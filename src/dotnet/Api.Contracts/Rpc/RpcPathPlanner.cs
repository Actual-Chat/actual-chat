namespace ActualChat.Rpc;

public static class RpcPathPlanner
{
    // A host takes the lead over an earlier one in the list only by a clear margin
    public const double Margin = 0.25;
    public const double KnownGoodScore = 0.9;
    public const double KnownBadScore = 0.5;
    public static readonly TimeSpan DefaultStartNextAfter = TimeSpan.FromSeconds(1.5);
    // A pass is trusted for a day: past that, the others are looked at again, which is how a client
    // that moved to a host during an outage finds its way back to the origin
    public static readonly TimeSpan PassedStaleAfter = TimeSpan.FromHours(24);
    public static readonly TimeSpan FailedStaleAfter = TimeSpan.FromHours(1);

    // mustVerify is for a new network: what was checked on an earlier visit doesn't vouch for it
    public static RpcPathPlan Plan(
        IReadOnlyList<string> candidates,
        RpcPathHistory history,
        DateTimeOffset now,
        bool mustVerify = false)
    {
        if (candidates.Count == 0)
            throw new ArgumentException("At least one candidate is required.", nameof(candidates));

        var scores = candidates.Distinct().ToDictionary(x => x, x => history.GetScore(x, now));
        var order = SortHosts(scores.Keys.ToList(), scores);
        var first = order[0];
        var firstChecks = history.GetChecks(first);
        var firstScore = scores[first];
        var hasChecks = firstChecks.Count > 0;
        var isKnownGood = hasChecks && !IsStale(firstChecks[^1], now) && firstScore >= KnownGoodScore;
        var isKnownBad = hasChecks && firstScore < KnownBadScore;

        TimeSpan? startNextAfter = order.Count < 2 || isKnownGood ? null
            : isKnownBad ? TimeSpan.Zero
            : DefaultStartNextAfter;
        var hostsToRefresh = mustVerify ? order.Skip(1).ToList()
            : isKnownGood ? []
            : order.Skip(1).Where(x => IsStale(history, x, now)).ToList();
        return new RpcPathPlan(order, isKnownGood, startNextAfter, hostsToRefresh);
    }

    public static bool IsFreshPass(RpcPathHistory history, string host, DateTimeOffset now)
    {
        var checks = history.GetChecks(host);
        return checks.Count > 0 && checks[^1].IsOk && !IsStale(checks[^1], now);
    }

    // Private methods

    private static List<string> SortHosts(List<string> rest, Dictionary<string, double> scores)
    {
        var order = new List<string>();
        while (rest.Count > 0) {
            // The earliest in the list among those within the margin of the best
            var best = rest.Max(x => scores[x]);
            var next = rest.First(x => scores[x] >= best - Margin);
            order.Add(next);
            rest.Remove(next);
        }
        return order;
    }

    private static bool IsStale(RpcPathHistory history, string host, DateTimeOffset now)
    {
        var checks = history.GetChecks(host);
        return checks.Count == 0 || IsStale(checks[^1], now);
    }

    private static bool IsStale(RpcPathCheck check, DateTimeOffset now)
    {
        var age = now - DateTimeOffset.FromUnixTimeMilliseconds(check.AtMs);
        return age >= (check.IsOk ? PassedStaleAfter : FailedStaleAfter);
    }
}
