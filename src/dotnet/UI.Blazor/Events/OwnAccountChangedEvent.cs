using ActualChat.Users;

namespace ActualChat.UI.Blazor.Events;

public sealed record OwnAccountChangedEvent(AccountFull Account, AccountFull OldAccount) : IUIEvent;
