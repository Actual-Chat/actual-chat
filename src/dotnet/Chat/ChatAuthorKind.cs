namespace ActualChat.Chat;

[DataContract]
public enum ChatAuthorKind
{
    [EnumMember] Any, /* Regular and Anonymous */
    [EnumMember] RegularOnly,
    /* In future we can support chats that allows anonymous authors only */
}
