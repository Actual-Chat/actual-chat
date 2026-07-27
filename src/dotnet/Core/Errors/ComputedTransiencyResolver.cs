using System.Data.Common;
using System.Net.Sockets;
using ActualLab.Resilience;

namespace ActualChat;

/// <summary>
/// Transiency policy for <see cref="Computed"/> error caching: unlike
/// <see cref="TransiencyResolvers.PreferTransient"/>, it treats unrecognized errors as
/// <see cref="Transiency.NonTransient"/>.
/// </summary>
public static class ComputedTransiencyResolver
{
    // PreferTransient's catch-all arm is Transient, so any error type it doesn't list -
    // which is nearly all of ours, NotFoundException included - gets retried at
    // TransientErrorInvalidationDelay (1s) forever, since a cached error keeps failing
    // for the same input. Only errors that a retry can plausibly fix opt into Transient here.
    // NOTE: this must stay scoped to TransiencyResolver<Computed>. TransiencyResolvers.PreferTransient
    // also backs TransiencyResolver<TDbContext>, which DbEntityResolver uses to decide DB retries.
    public static readonly TransiencyResolver Instance = static e
        => TransiencyResolvers.CoreOnly.Invoke(e).Or(e, static e => e switch {
            DbException => Transiency.Transient,
            SocketException => Transiency.Transient,
            HttpRequestException => Transiency.Transient,
            _ => Transiency.NonTransient,
        });
}
