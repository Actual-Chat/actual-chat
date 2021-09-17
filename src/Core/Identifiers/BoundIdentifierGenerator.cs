using System;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat
{
    public readonly struct BoundIdentifierGenerator<TMasterIdentifier> : IBoundIdentifierGenerator<TMasterIdentifier>
        where TMasterIdentifier : struct, IMasterIdentifier
    {
        private readonly TMasterIdentifier _master;
        private readonly IServiceProvider _serviceProvider;

        public BoundIdentifierGenerator(TMasterIdentifier master, IServiceProvider serviceProvider)
        {
            _master = master;
            _serviceProvider = serviceProvider;
        }

        public TIdentifier Next<TIdentifier>() where TIdentifier : struct, ISlaveIdentifier<TMasterIdentifier>
            => _serviceProvider.GetRequiredService<ISlaveIdentifierGenerator<TIdentifier, TMasterIdentifier>>()
                .Next(_master);
    }
}