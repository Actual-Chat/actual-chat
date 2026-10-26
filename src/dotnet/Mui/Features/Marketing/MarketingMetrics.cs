using ActualChat.Users;

namespace ActualChat.Mui;

public static class MarketingMetrics
{
    // The attributions first seen in the window, per campaign; one chart is made for each row
    public const string CampaignListSql = """
        select platform, campaign, count(distinct user_id) as users
        from user_attributions
        where campaign <> '' and first_seen_at >= {from} and first_seen_at < {to}
        group by platform, campaign
        order by users desc, platform, campaign
        """;

    // The same per keyword: Google sends the search term, Apple only the keyword id
    public const string KeywordListSql = """
        select platform, term, keyword_id, count(distinct user_id) as users
        from user_attributions
        where (term <> '' or keyword_id <> '') and first_seen_at >= {from} and first_seen_at < {to}
        group by platform, term, keyword_id
        order by users desc, platform, term, keyword_id
        """;

    // Usually reached in this order; a kind missing here goes after them
    private static readonly MilestoneKind[] MilestoneOrder = [
        MilestoneKind.Onboarded,
        MilestoneKind.Message,
        MilestoneKind.PeerChat,
        MilestoneKind.Conversation,
        MilestoneKind.Day2Return,
    ];

    public static IReadOnlyList<MuiMetric> Milestones { get; } = [
        new("sign_ups", "New accounts (sign-ups)",
            "select count(*) from usage_events where kind = 5 and occurred_at >= {from} and occurred_at < {to}",
            Description: "Usage events of kind 5 (SignUp) in the window: one is written when an account is created. "
                + "Shown here as the base the milestone counts compare to."),
        .. MilestoneOrder
            .Concat(Enum.GetValues<MilestoneKind>().Except(MilestoneOrder))
            .Select(GetMilestoneMetric),
    ];

    public static MuiMetric CreateCampaignMetric(string?[] row, int index)
    {
        var (platform, campaign) = (row[0] ?? "", row[1] ?? "");
        var condition = $"platform = {MuiMetricSql.Literal(platform)} and campaign = {MuiMetricSql.Literal(campaign)}";
        return new MuiMetric($"campaign_{index}", WithPlatform(campaign, platform),
            GetUserCountSql(condition),
            Description: $"Distinct users with an attribution of campaign \"{campaign}\" on platform \"{platform}\" "
                + "first seen in the window (user_attributions.first_seen_at): a user who comes through the same "
                + "campaign again is counted in the window of the first visit only. "
                + "The campaigns shown are those that brought someone in the latest window.");
    }

    public static MuiMetric CreateKeywordMetric(string?[] row, int index)
    {
        var (platform, term, keywordId) = (row[0] ?? "", row[1] ?? "", row[2] ?? "");
        var condition = $"platform = {MuiMetricSql.Literal(platform)} and term = {MuiMetricSql.Literal(term)}"
            + $" and keyword_id = {MuiMetricSql.Literal(keywordId)}";
        var keyword = term.IsNullOrEmpty() ? $"keyword {keywordId}" : term;
        return new MuiMetric($"keyword_{index}", WithPlatform(keyword, platform),
            GetUserCountSql(condition),
            Description: $"Distinct users with an attribution of {keyword} on platform \"{platform}\" first seen in "
                + "the window (user_attributions.first_seen_at), across all campaigns. Google sends the search term, "
                + "Apple only the keyword id. The keywords shown are those that brought someone in the latest window.");
    }

    // Private methods

    private static MuiMetric GetMilestoneMetric(MilestoneKind kind)
    {
        var eventName = kind.GetEventName();
        var meaning = kind switch {
            MilestoneKind.Message => "the first post outside the Notes chat",
            MilestoneKind.PeerChat => "the first post in a 1:1 chat",
            MilestoneKind.Conversation => "a post in a chat where another human posted among the last entries too",
            MilestoneKind.Day2Return => "posts on a second UTC day within 48 h of sign-up",
            MilestoneKind.Onboarded => "onboarding completed",
            _ => "",
        };
        var retention = kind.GetValue() is { } value
            ? $" About {value}% of the users who reach it are still active on days 60-90."
            : "";
        return new MuiMetric($"milestone_{eventName}", "Reached: " + eventName.Replace('_', ' '),
            $"select count(*) from user_milestones where kind = {(int)kind}"
            + " and reached_at >= {from} and reached_at < {to}",
            Description: $"Users who reached the {kind} milestone ({meaning}) in the window: user_milestones rows "
                + $"of kind {(int)kind} by reached_at. Each user reaches a milestone once, so this counts users. "
                + $"GA4 gets it as the \"{eventName}\" event.{retention}");
    }

    private static string GetUserCountSql(string condition)
        => "select count(distinct user_id) from user_attributions where " + condition
            + " and first_seen_at >= {from} and first_seen_at < {to}";

    private static string WithPlatform(string title, string platform)
        => platform.IsNullOrEmpty() ? title : $"{title} ({platform})";
}
