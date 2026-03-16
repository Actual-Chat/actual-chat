using dotAPNS;

namespace ActualChat.Notification;

public static class ApplePushExt
{
    public static ApplePush Add(this ApplePush push, string key, string? value)
    {
        if (value is not null)
            push.AddCustomProperty(key, value);
        return push;
    }
}
