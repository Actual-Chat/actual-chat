using System;

namespace ActualChat.Chat
{
    [Flags]
    public enum ChatPermissions
    {
        Read = 0x1,
        Write = 0x4 | Read,
        Owner = 0x1000 | Write
    }
}
