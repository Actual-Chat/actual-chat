using System;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Users.Db;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users
{
    public class SpeakerStateService : DbServiceBase<UsersDbContext>, ISpeakerStateService
    {
        protected IDbEntityResolver<string, DbSpeakerState> DbSpeakerStateResolver { get; }

        public SpeakerStateService(IServiceProvider services)
            : base(services)
            => DbSpeakerStateResolver = services.DbEntityResolver<string, DbSpeakerState>();

        [ComputeMethod(AutoInvalidateTime = 61)]
        public virtual async Task<bool> IsOnline(string speakerId, CancellationToken cancellationToken = default)
        {
            var cutoffTime = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
            var speakerState = await DbSpeakerStateResolver.TryGet(speakerId, cancellationToken);
            return speakerState?.OnlineCheckInAt > cutoffTime;
        }
    }
}
