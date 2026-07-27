using ActualChat.Rpc;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;
using ActualLab.Rpc.Middlewares;

namespace ActualChat.Resilience.Internal;

/// <summary>
/// Charges inbound API commands against the <see cref="RateLimitPolicy"/> before they're executed.
/// Reads, backend and system calls are passed through untouched.
/// </summary>
public sealed class RpcRateLimitMiddleware(IServiceProvider services) : IRpcMiddleware
{
    private IServiceProvider Services { get; } = services;
    private RateLimitPolicy Policy => field ??= Services.GetService<RateLimitPolicy>() ?? RateLimitPolicy.Unlimited;
    private RateLimitIdentityResolver IdentityResolver
        => field ??= Services.GetService<RateLimitIdentityResolver>() ?? new RateLimitIdentityResolver(Services);
    private RateLimitClassResolver ClassResolver
        => field ??= Services.GetService<RateLimitClassResolver>() ?? RateLimitClassResolver.Default;

    // Runs after RpcDefaultSessionReplacer, which resolves default sessions to real ones
    public double Priority { get; init; } = RpcInboundMiddlewarePriority.ArgumentValidation - 2;

    public Func<RpcInboundCall, Task<T>> Create<T>(
        RpcMiddlewareContext<T> context,
        Func<RpcInboundCall, Task<T>> next)
    {
        var methodDef = context.MethodDef;
        if (methodDef.IsSystem || methodDef.IsBackend || methodDef.Kind is RpcMethodKind.Other)
            return next;

        var arg0Type = methodDef.Parameters.GetValueOrDefault(0)?.ParameterType;
        var isCommand = methodDef.Kind is RpcMethodKind.Command;
        if (ClassResolver.Resolve(arg0Type?.Name ?? "", isCommand) is not { } rateLimitClass)
            return next;

        var sessionGetter = GetSessionGetter(arg0Type);
        var method = methodDef.FullName;
        return async call => {
            await Check(call, method, rateLimitClass, sessionGetter).ConfigureAwait(false);
            return await next.Invoke(call).ConfigureAwait(false);
        };
    }

    // Private methods

    private static Func<ArgumentList, Session?>? GetSessionGetter(Type? arg0Type)
    {
        if (arg0Type == typeof(Session))
            return args => (Session?)args.Get0Untyped();
        if (typeof(ISessionCommand).IsAssignableFrom(arg0Type))
            return args => ((ISessionCommand?)args.Get0Untyped())?.Session;

        return null;
    }

    private async Task Check(
        RpcInboundCall call,
        string method,
        RateLimitClass rateLimitClass,
        Func<ArgumentList, Session?>? sessionGetter)
    {
        var cancellationToken = call.CallCancelToken;
        var session = call.Arguments is { } arguments ? sessionGetter?.Invoke(arguments) : null;
        var source = new RateLimitSource(session, call.Context.GetRemoteIPAddress());
        var identities = new RateLimitIdentity[RateLimitIdentityResolver.MaxIdentityCount];
        var identityCount = await IdentityResolver
            .Resolve(Policy, rateLimitClass, source, identities, cancellationToken)
            .ConfigureAwait(false);
        if (identityCount == 0)
            return;

        await Policy
            .Check(method, rateLimitClass, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);
    }
}
