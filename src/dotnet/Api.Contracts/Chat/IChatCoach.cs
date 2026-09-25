namespace ActualChat.Chat;

/// <summary>
/// The caller's own speech-coach marks in a chat; nothing here ever exposes another author's spans.
/// </summary>
public interface IChatCoach : IComputeService
{
    [ComputeMethod]
    Task<bool> IsEnabled(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CoachEntryMarks>> GetOwnMarks(
        Session session, ChatId chatId, Range<long> lidRange, CancellationToken cancellationToken);
}
