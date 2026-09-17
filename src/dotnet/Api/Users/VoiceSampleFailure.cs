namespace ActualChat.Users;

/// <summary>
/// Why a voice clone's reference clip can't be made from what the user has.
/// </summary>
public enum VoiceSampleFailure
{
    None = 0,
    NotEnoughRecordings,
    SampleMissing,
}
