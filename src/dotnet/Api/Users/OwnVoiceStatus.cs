namespace ActualChat.Users;

/// <summary>
/// What the "Use my own voice" setting shows: the consent, the clone's state, and what's missing
/// for the sample. <see cref="MissingDuration"/> is for the auto sample only - null with an explicit one.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record OwnVoiceStatus(
    [property: DataMember, Key(0)] bool IsEnabled,
    [property: DataMember, Key(1)] UserVoiceStatus Status,
    [property: DataMember, Key(2)] VoiceSampleFailure Failure,
    [property: DataMember, Key(3)] TimeSpan? MissingDuration,
    [property: DataMember, Key(4)] bool HasExplicitSample
)
{
    public static readonly OwnVoiceStatus Off = new(false, UserVoiceStatus.None, VoiceSampleFailure.None, null, false);
}
