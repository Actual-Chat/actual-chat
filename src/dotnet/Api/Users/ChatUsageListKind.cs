namespace ActualChat.Users;

[Obsolete("2026.10: The search panel's recents and voice samples come from contacts. Old clients only.")]
public enum ChatUsageListKind
{
    PeerChatsWroteTo = 0,
    ViewedGroupChats = 1,
    SearchChats = 2,
}
