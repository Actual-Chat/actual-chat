using ActualChat.Users;

namespace ActualChat.Mui.UnitTests;

public sealed class MarketingMetricsTest
{
    [Fact]
    public void LiteralShouldEscapeQuotes()
    {
        // act
        var literal = MuiMetricSql.Literal("it's'); drop table x; --");

        // assert
        literal.Should().Be("'it''s''); drop table x; --'");
    }

    [Fact]
    public void MilestonesShouldCoverEveryKindOnce()
    {
        // act
        var keys = MarketingMetrics.Milestones.Select(m => m.Key).ToArray();

        // assert
        keys[0].Should().Be("sign_ups");
        keys.Skip(1).Should().BeEquivalentTo(
            Enum.GetValues<MilestoneKind>().Select(k => "milestone_" + k.GetEventName()));
        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CampaignMetricShouldFilterByPlatformAndCampaign()
    {
        // act
        var metric = MarketingMetrics.CreateCampaignMetric(["google", "o'brien-spring"], 3);

        // assert
        metric.Key.Should().Be("campaign_3");
        metric.Title.Should().Be("o'brien-spring (google)");
        metric.Sql.Should().Contain("platform = 'google' and campaign = 'o''brien-spring'");
        metric.Sql.Should().Contain("first_seen_at >= {from} and first_seen_at < {to}");
    }

    [Fact]
    public void KeywordMetricShouldNameAppleKeywordsById()
    {
        // act
        var google = MarketingMetrics.CreateKeywordMetric(["google", "walkie talkie", ""], 0);
        var apple = MarketingMetrics.CreateKeywordMetric(["apple", "", "12323222"], 1);

        // assert
        google.Title.Should().Be("walkie talkie (google)");
        apple.Title.Should().Be("keyword 12323222 (apple)");
        apple.Sql.Should().Contain("term = '' and keyword_id = '12323222'");
    }
}
