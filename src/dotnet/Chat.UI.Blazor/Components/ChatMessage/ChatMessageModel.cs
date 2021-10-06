using ActualChat.Users;
namespace ActualChat.Chat.UI.Blazor;

public record ChatMessageModel(UserInfo? UserInfo, bool? IsOnline);