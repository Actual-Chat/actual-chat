using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.EntityFramework;
using Stl.Time;

namespace ActualChat.Users
{
    public class SpeakerStateService : DbServiceBase<UsersDbContext>, ISpeakerStateService
    {
        protected DbEntityResolver<UsersDbContext, string, DbSpeakerState> DbSpeakerStateResolver { get; }

        public SpeakerStateService(IServiceProvider services)
            : base(services)
            => DbSpeakerStateResolver = services.GetRequiredService<DbEntityResolver<UsersDbContext, string, DbSpeakerState>>();

        [ComputeMethod(AutoInvalidateTime = 61)]
        public virtual async Task<bool> IsOnline(string speakerId, CancellationToken cancellationToken = default)
        {
            var cutoffTime = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
            var speakerState = await DbSpeakerStateResolver.TryGet(speakerId, cancellationToken);
            return speakerState?.LastOnlineAt > cutoffTime;
        }
    }
}
