using System;

namespace ActualChat.Chat
{
    [Flags]
    public enum ChatPermission
    {
        Read = 0x1,
        Write = 0x4 | Read,
        Owner = 0x100 | Write
    }
}
