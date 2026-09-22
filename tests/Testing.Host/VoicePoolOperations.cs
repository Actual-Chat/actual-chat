using ActualChat.Streaming.Services;
using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class VoicePoolOperations
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(30);

    public static async Task<string?> AcquireSettled(
        this VoicePool pool,
        UserId userId,
        CancellationToken cancellationToken)
    {
        // Acquire never waits for the work it starts: null now, the clone on the next call. This is
        // that next call - the answer once nothing is in flight, so a test sees the attempt's outcome
        var voiceId = await pool.Acquire(userId, cancellationToken).ConfigureAwait(false);
        if (voiceId != null)
            return voiceId;

        await pool.WhenSettled().ConfigureAwait(false);
        voiceId = await pool.Acquire(userId, cancellationToken).ConfigureAwait(false);
        if (voiceId == null)
            await pool.WhenSettled().ConfigureAwait(false);
        return voiceId;
    }

    public static Task WhenSettled(this VoicePool pool)
        => TestWait.WhenPolled(() => pool.InFlightCount.Should().Be(0), SettleTimeout);
}
