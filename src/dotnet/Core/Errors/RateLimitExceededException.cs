using System.Text.RegularExpressions;
using ActualLab.Resilience;

namespace ActualChat;

/// <summary>
/// Exception thrown when a call exceeds a rate limit. Its <see cref="RetryDelay"/> is encoded
/// into the message, since <see cref="ExceptionInfo"/> transmits the type and the message only.
/// </summary>
public sealed partial class RateLimitExceededException : Exception, ITransientException, IHasRetryDelay
{
    private const string DefaultMessage = "Too many requests.";

    [GeneratedRegex(@"Retry in (?<seconds>\d+)s\.$", RegexOptions.ExplicitCapture)]
    private static partial Regex RetryDelayRegexFactory();
    private static readonly Regex RetryDelayRegex = RetryDelayRegexFactory();

    public TimeSpan RetryDelay { get; }

    public RateLimitExceededException() : this(null, null) { }
    public RateLimitExceededException(TimeSpan retryDelay)
        : base(FormatMessage(retryDelay))
        => RetryDelay = retryDelay;
    public RateLimitExceededException(string? message) : this(message, null) { }
    public RateLimitExceededException(string? message, Exception? innerException)
        : base(message ?? DefaultMessage, innerException)
        => RetryDelay = ParseRetryDelay(message);

    // Private methods

    private static string FormatMessage(TimeSpan retryDelay)
        => $"{DefaultMessage} Retry in {ToWholeSeconds(retryDelay)}s.";

    // Rounded up, so a round-tripped delay is never shorter than the one the server meant
    private static int ToWholeSeconds(TimeSpan retryDelay)
        => (int)Math.Clamp(Math.Ceiling(retryDelay.TotalSeconds), 1, int.MaxValue);

    private static TimeSpan ParseRetryDelay(string? message)
    {
        if (message.IsNullOrEmpty())
            return TimeSpan.Zero;

        var match = RetryDelayRegex.Match(message);
        return match.Success && int.TryParse(match.Groups["seconds"].ValueSpan, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }
}
