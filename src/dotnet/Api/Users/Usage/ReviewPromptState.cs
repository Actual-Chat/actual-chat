namespace ActualChat.Users;

public enum ReviewPromptOutcome
{
    None = 0,
    // The platform confirmed a completed review; terminal
    Reviewed = 1,
    // The native sheet or the store page was requested, with no way to learn what happened next
    Asked = 2,
    // The user said no on the modal's own screens
    Declined = 3,
    // The modal was closed any other way
    Dismissed = 4,
}

/// <summary>
/// Whether the app may show the review prompt right now, and why not when it may not.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ReviewPromptState(
    [property: DataMember, Key(0)] bool CanPrompt,
    [property: DataMember, Key(1)] string Reason
);

/// <summary>
/// A review prompt the server decided to show: which live session earned it and when.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PendingReviewPrompt(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] Moment Since
);
