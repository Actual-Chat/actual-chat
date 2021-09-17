using System;

namespace ActualChat
{
    public interface IIdentifier
    { }
    
    public interface IMasterIdentifier : IIdentifier
    { }

    public interface ISlaveIdentifier<TMasterIdentifier> : IIdentifier
        where TMasterIdentifier : struct, IMasterIdentifier
    { }

    public interface IIdentifier<out TValue> : IIdentifier where TValue : IEquatable<TValue>
    {
        public TValue Value { get; }
    }
}
