namespace ActualChat.Transcription;

/// <summary>
/// Specifies how DTW alignment should handle leading/trailing trims.
/// </summary>
public enum LinearMapAlignmentMode
{
    RetranscribeSameAudio, // Trims presumed zero, tighter band
    UserEditedTranscript, // Detect leading/trailing trims
}
