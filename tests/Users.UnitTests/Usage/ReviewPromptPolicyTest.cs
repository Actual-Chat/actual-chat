using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Usage;

public class ReviewPromptPolicyTest
{
    private static readonly Moment Now = new(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));
    private static readonly ReviewPromptSettings Settings = new();
    private static readonly UsageSummary Qualifying = new() {
        ActiveDays = 3,
        SpeechMs = 5 * 60_000,
        LiveSessions = 1,
    };
    private static readonly Moment OldEnough = Now - TimeSpan.FromDays(10);

    [Fact]
    public void QualifyingUserShouldBeEligible()
    {
        // act
        var state = ReviewPromptPolicy.Evaluate(Settings, Now, OldEnough, Qualifying, new AppReviewPromptState());

        // assert
        state.CanPrompt.Should().BeTrue(state.Reason);
    }

    [Theory]
    [InlineData("age")]
    [InlineData("days")]
    [InlineData("speech")]
    [InlineData("sessions")]
    public void EachThresholdAloneShouldBlock(string missing)
    {
        // arrange
        var createdAt = missing == "age" ? Now - TimeSpan.FromDays(2) : OldEnough;
        var summary = missing switch {
            "days" => Qualifying with { ActiveDays = 2 },
            "speech" => Qualifying with { SpeechMs = 5 * 60_000 - 1 },
            "sessions" => Qualifying with { LiveSessions = 0 },
            _ => Qualifying,
        };

        // act
        var state = ReviewPromptPolicy.Evaluate(Settings, Now, createdAt, summary, new AppReviewPromptState());

        // assert
        state.CanPrompt.Should().BeFalse(missing);
    }

    [Fact]
    public void ReviewedShouldBeTerminal()
    {
        // arrange
        var history = new AppReviewPromptState {
            Outcome = ReviewPromptOutcome.Reviewed,
            LastPromptedAt = Now - TimeSpan.FromDays(400),
        };

        // act
        var state = ReviewPromptPolicy.Evaluate(Settings, Now, OldEnough, Qualifying, history);

        // assert
        state.CanPrompt.Should().BeFalse("a confirmed review is terminal");
        state.Reason.Should().Be("Already reviewed");
    }

    [Fact]
    public void MaxDeclinesShouldBeTerminal()
    {
        // arrange
        var history = new AppReviewPromptState {
            Outcome = ReviewPromptOutcome.Declined,
            DeclineCount = 2,
            LastPromptedAt = Now - TimeSpan.FromDays(400),
        };

        // act
        var state = ReviewPromptPolicy.Evaluate(Settings, Now, OldEnough, Qualifying, history);

        // assert
        state.CanPrompt.Should().BeFalse("two declines end the prompting");
    }

    [Theory]
    [InlineData(ReviewPromptOutcome.Asked)]
    [InlineData(ReviewPromptOutcome.Declined)]
    [InlineData(ReviewPromptOutcome.Dismissed)]
    public void NonTerminalOutcomeShouldRetryAfterTheWindow(ReviewPromptOutcome outcome)
    {
        // arrange
        var history = new AppReviewPromptState {
            Outcome = outcome,
            DeclineCount = outcome == ReviewPromptOutcome.Declined ? 1 : 0,
        };
        var justBefore = history with { LastPromptedAt = Now - Settings.RetryAfter + TimeSpan.FromHours(1) };
        var justAfter = history with { LastPromptedAt = Now - Settings.RetryAfter - TimeSpan.FromHours(1) };

        // act
        var blocked = ReviewPromptPolicy.Evaluate(Settings, Now, OldEnough, Qualifying, justBefore);
        var allowed = ReviewPromptPolicy.Evaluate(Settings, Now, OldEnough, Qualifying, justAfter);

        // assert
        blocked.CanPrompt.Should().BeFalse();
        allowed.CanPrompt.Should().BeTrue(allowed.Reason);
    }

    [Fact]
    public void MinIntervalShouldHoldEvenWithAShortRetryWindow()
    {
        // arrange
        var settings = new ReviewPromptSettings {
            RetryAfter = TimeSpan.FromDays(1),
            MinInterval = TimeSpan.FromDays(30),
        };
        var history = new AppReviewPromptState {
            Outcome = ReviewPromptOutcome.Asked,
            LastPromptedAt = Now - TimeSpan.FromDays(5),
        };

        // act
        var state = ReviewPromptPolicy.Evaluate(settings, Now, OldEnough, Qualifying, history);

        // assert
        state.CanPrompt.Should().BeFalse("MinInterval holds regardless of RetryAfter");
    }

    [Fact]
    public void ApplyShouldCountPromptsAndDeclines()
    {
        // act
        var asked = ReviewPromptPolicy.Apply(new AppReviewPromptState(), ReviewPromptOutcome.Asked, Now);
        var declined = ReviewPromptPolicy.Apply(asked, ReviewPromptOutcome.Declined, Now + TimeSpan.FromDays(61));

        // assert
        asked.PromptCount.Should().Be(1);
        asked.DeclineCount.Should().Be(0);
        asked.Outcome.Should().Be(ReviewPromptOutcome.Asked);
        asked.LastPromptedAt.Should().Be(Now);
        declined.PromptCount.Should().Be(2);
        declined.DeclineCount.Should().Be(1);
        declined.Outcome.Should().Be(ReviewPromptOutcome.Declined);
    }

    [Fact]
    public void ApplyShouldClearThePendingPrompt()
    {
        // arrange
        var chatId = ChatId.Parse("the-actual-one");
        var pending = ReviewPromptPolicy.MarkPending(new AppReviewPromptState(), chatId, Now);

        // act
        var fresh = ReviewPromptPolicy.GetPending(pending, TimeSpan.FromMinutes(10), Now + TimeSpan.FromMinutes(9));
        var stale = ReviewPromptPolicy.GetPending(pending, TimeSpan.FromMinutes(10), Now + TimeSpan.FromMinutes(11));
        var recorded = ReviewPromptPolicy.Apply(pending, ReviewPromptOutcome.Dismissed, Now + TimeSpan.FromMinutes(1));

        // assert
        fresh.Should().Be(new PendingReviewPrompt(chatId, Now));
        stale.Should().BeNull("a prompt older than the TTL must not pop up later");
        recorded.PendingSince.Should().BeNull();
        recorded.PendingChatId.Should().BeNull();
    }

    [Fact]
    public void ApplyShouldKeepReviewedSticky()
    {
        // arrange
        var reviewed = ReviewPromptPolicy.Apply(new AppReviewPromptState(), ReviewPromptOutcome.Reviewed, Now);

        // act
        var later = ReviewPromptPolicy.Apply(reviewed, ReviewPromptOutcome.Dismissed, Now + TimeSpan.FromDays(1));

        // assert
        later.Outcome.Should().Be(ReviewPromptOutcome.Reviewed);
    }
}
