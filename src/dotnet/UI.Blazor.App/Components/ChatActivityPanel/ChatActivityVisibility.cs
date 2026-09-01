namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Decides whether a chat has activity worth showing the activity panel for.
/// Shared by the panel and its header wrapper, which would otherwise disagree
/// and leave an empty wrapper on screen.
/// </summary>
public static class ChatActivityVisibility
{
    public static bool HasActivity(
        bool isPttArmed,
        bool isListening = false,
        bool isRecording = false,
        bool isAnyoneTalking = false,
        bool isOwnVideoStreaming = false,
        bool hasRemoteStreams = false,
        bool isSharingOwnLocation = false)
        // Arming a chat pins IsListening on for as long as it stays armed, so a bare listen there
        // is ambient state rather than an activity - and the panel it would open has nothing to draw.
        => isRecording
            || isAnyoneTalking
            || isOwnVideoStreaming
            || hasRemoteStreams
            || isSharingOwnLocation
            || (isListening && !isPttArmed);
}
