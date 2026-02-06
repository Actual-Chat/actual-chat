namespace ActualChat;

/// <summary>
/// Specifies the type of chat entry content.
/// </summary>
public enum ChatEntryKind
{
    Text = 0,
    Audio,
    Transcribed,
    //Video, // Video chat entries are invalid so far.
}
