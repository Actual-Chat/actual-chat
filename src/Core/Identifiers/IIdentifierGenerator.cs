using System;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat
{
    public interface IIdentifierGenerator<out TIdentifier> where TIdentifier :  struct, IMasterIdentifier
    {
        TIdentifier Next();
    }

    public interface ISlaveIdentifierGenerator<out TIdentifier, in TMasterIdentifier>
        where TIdentifier : struct, ISlaveIdentifier<TMasterIdentifier>
        where TMasterIdentifier : struct, IMasterIdentifier
    {
        TIdentifier Next(TMasterIdentifier master);
    }

    public interface IBoundIdentifierGenerator<TMasterIdentifier>
        where TMasterIdentifier : struct, IMasterIdentifier
    {
        public TIdentifier Next<TIdentifier>() where TIdentifier : struct, ISlaveIdentifier<TMasterIdentifier>;
    }
    
    public interface IIdentifierGenerator
    {
        TIdentifier Next<TIdentifier>() where TIdentifier : struct, IMasterIdentifier;

        TIdentifier Next<TIdentifier, TMasterIdentifier>(TMasterIdentifier master)
            where TIdentifier : struct, ISlaveIdentifier<TMasterIdentifier>
            where TMasterIdentifier : struct, IMasterIdentifier;

        BoundIdentifierGenerator<TMasterIdentifier> InScopeOf<TMasterIdentifier>(TMasterIdentifier master)
            where TMasterIdentifier : struct, IMasterIdentifier;
    }
}