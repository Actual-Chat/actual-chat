namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class HeadsetButtonPolicy
{
    public static HeadsetButtonAction Decide(
        HeadsetKey key,
        bool isDown,
        int repeatCount,
        bool isEnabled,
        bool hasAnswerWindow,
        bool isReplyHot)
    {
        // One press delivers both edges plus auto-repeats; acting on more than one would
        // open the mic and immediately close it, because the later edges see a hot reply.
        if (!isDown || repeatCount != 0)
            return HeadsetButtonAction.PassThrough;
        if (!isEnabled || key == HeadsetKey.Unknown)
            return HeadsetButtonAction.PassThrough;
        // A reply can outlive the answer window, so closing it must not depend on the window.
        if (isReplyHot)
            return HeadsetButtonAction.StopReply;

        return hasAnswerWindow ? HeadsetButtonAction.StartReply : HeadsetButtonAction.PassThrough;
    }
}

public enum HeadsetKey
{
    Unknown = 0,
    Hook,
    PlayPause,
}

public enum HeadsetButtonAction
{
    PassThrough = 0,
    StartReply,
    StopReply,
}
