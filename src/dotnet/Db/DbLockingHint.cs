namespace ActualChat.Db;

public enum DbLockingHint
{
    None = 0,
    Update,
    NoKeyUpdate,
    Share,
    KeyShare,
}
