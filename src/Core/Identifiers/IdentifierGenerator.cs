using System;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat
{
    public sealed class IdentifierGenerator : IIdentifierGenerator
    {
        private readonly IServiceProvider _serviceProvider;

        public IdentifierGenerator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public TIdentifier Next<TIdentifier>() where TIdentifier : struct, IMasterIdentifier 
            => _serviceProvider.GetRequiredService<IIdentifierGenerator<TIdentifier>>().Next();

        public TIdentifier Next<TIdentifier, TMasterIdentifier>(TMasterIdentifier master)
            where TIdentifier : struct, ISlaveIdentifier<TMasterIdentifier>
            where TMasterIdentifier : struct, IMasterIdentifier
            => _serviceProvider.GetRequiredService<ISlaveIdentifierGenerator<TIdentifier, TMasterIdentifier>>()
                .Next(master);

        public BoundIdentifierGenerator<TMasterIdentifier> InScopeOf<TMasterIdentifier>(TMasterIdentifier master)
            where TMasterIdentifier : struct, IMasterIdentifier
            => new(master, _serviceProvider);
    }
}