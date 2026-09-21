using ActualChat.Users.Module;

namespace ActualChat.Users;

/// <summary>
/// The review-prompt rules over a user's history and usage; pure, so the thresholds are testable.
/// </summary>
public static class ReviewPromptPolicy
{
    public static ReviewPromptState Evaluate(
        ReviewPromptSettings settings,
        Moment now,
        Moment accountCreatedAt,
        UsageSummary summary,
        AppReviewPromptState history)
    {
        if (history.Outcome == ReviewPromptOutcome.Reviewed)
            return new(false, "Already reviewed");
        if (history.DeclineCount >= settings.MaxDeclines)
            return new(false, "Declined too many times");
        if (history.LastPromptedAt is { } lastPromptedAt) {
            var retryAt = lastPromptedAt + settings.RetryAfter;
            if (now < retryAt)
                return new(false, $"Prompted recently, next attempt at {retryAt:u}");

            var intervalEndsAt = lastPromptedAt + settings.MinInterval;
            if (now < intervalEndsAt)
                return new(false, $"Within the minimum interval until {intervalEndsAt:u}");
        }

        if (now - accountCreatedAt < settings.MinAccountAge)
            return new(false, $"Account younger than {settings.MinAccountAge.TotalDays:0} days");
        if (summary.ActiveDays < settings.MinActiveDays)
            return new(false, $"Active days: {summary.ActiveDays} < {settings.MinActiveDays}");
        if (summary.SpeechMs < settings.MinSpeechDuration.TotalMilliseconds)
            return new(false,
                $"Speech: {TimeSpan.FromMilliseconds(summary.SpeechMs):g} < {settings.MinSpeechDuration:g}");
        if (summary.LiveSessions < settings.MinLiveSessions)
            return new(false, $"Live sessions: {summary.LiveSessions} < {settings.MinLiveSessions}");

        return new(true, "Eligible");
    }

    public static AppReviewPromptState Apply(AppReviewPromptState history, ReviewPromptOutcome outcome, Moment now)
    {
        // A confirmed review is sticky: a later "not now" from another device must not reopen it.
        var isReviewed = history.Outcome == ReviewPromptOutcome.Reviewed || outcome == ReviewPromptOutcome.Reviewed;
        return history with {
            LastPromptedAt = now,
            PromptCount = history.PromptCount + 1,
            DeclineCount = history.DeclineCount + (outcome == ReviewPromptOutcome.Declined ? 1 : 0),
            Outcome = isReviewed ? ReviewPromptOutcome.Reviewed : outcome,
            PendingSince = null,
            PendingChatId = null,
        };
    }

    public static AppReviewPromptState MarkPending(AppReviewPromptState history, ChatId chatId, Moment now)
        => history with { PendingSince = now, PendingChatId = chatId };

    public static PendingReviewPrompt? GetPending(AppReviewPromptState history, TimeSpan ttl, Moment now)
        => history is { PendingSince: { } since, PendingChatId: { } chatId } && now - since < ttl
            ? new PendingReviewPrompt(chatId, since)
            : null;
}
