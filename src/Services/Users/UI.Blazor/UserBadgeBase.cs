using System.Threading;
using System.Threading.Tasks;
using ActualChat.Users.UI.Blazor.Models;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Authentication;
using Stl.Fusion.Blazor;

namespace ActualChat.Users.UI.Blazor
{
    public abstract class UserBadgeBase : ComputedStateComponent<UserBadgeModel>
    {
        [Inject] protected IUserInfoService UserInfos { get; set; } = null!;
        [Inject] protected IUserStateService UserStates { get; set; } = null!;
        [Inject] protected Session Session { get; set; } = null!;

        [Parameter]
        public string UserId { get; set; } = "";
        [Parameter]
        public bool DisplayOnlineState { get; set; } = true;

        protected override async Task<UserBadgeModel> ComputeState(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(UserId))
                return new UserBadgeModel();

            var userInfo = await UserInfos.TryGet(UserId, cancellationToken);
            var isOnline = (bool?) null;
            if (DisplayOnlineState && userInfo != null)
                isOnline = await UserStates.IsOnline(UserId, cancellationToken);
            return new UserBadgeModel() {
                UserInfo = userInfo,
                IsOnline = isOnline,
            };
        }
    }
}
