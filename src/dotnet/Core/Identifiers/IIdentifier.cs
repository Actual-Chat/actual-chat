using System;

namespace ActualChat
{
    public interface IIdentifier
    { }

    public interface IIdentifier<out TValue> : IIdentifier
        where TValue : IEquatable<TValue>
    {
        public TValue Value { get; }
    }
}
