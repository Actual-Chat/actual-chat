using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Stl.Fusion.Authentication;

namespace ActualChat.Distribution
{
    public class ChatMessageHub: Hub<IChatMessageHub>, IChatMessageHub
    {
        private readonly IAuthService _authService;
        
        // private readonly IHubContext<DistributionHub, IDistributionService> _hubContext;
        
        public ChatMessageHub(IAuthService authService)
        {
            _authService = authService;
            // _hubContext.Clients.All.
        }

        public async Task<ChannelReader<MessageVariant>> Subscribe(Session session, string chatId, CancellationToken cancellationToken)
        {
            var user = await _authService.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();
            
            // TODO: AK - we need authorization
            
            throw new System.NotImplementedException();
        }
    }
}