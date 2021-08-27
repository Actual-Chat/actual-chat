namespace ActualChat.Users.UI.Blazor.Models
{
    public record UserBadgeModel
    {
        public UserInfo? UserInfo { get; init; }
        public bool? IsOnline { get; init; }
    }
}
