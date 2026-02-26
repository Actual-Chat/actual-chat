namespace ActualChat;

/// <summary>
/// Specifies the type of chat entry content.
/// </summary>
public enum ChatEntryKind
{
    Text = 0,
    [Obsolete("Audio entries are no longer created. Audio data is stored in Media table. Kept for DB compatibility.")]
    Audio = 1,
    Transcribed,
    //Video, // Video chat entries are invalid so far.
}
