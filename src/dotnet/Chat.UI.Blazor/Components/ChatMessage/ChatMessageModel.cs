using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public record ChatMessageModel(UserInfo? UserInfo, bool? IsOnline);
