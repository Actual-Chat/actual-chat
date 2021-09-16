using System;

namespace ActualChat
{
    public interface IIdentifier
    { }

    public interface IIdentifier<out TKey> : IIdentifier where TKey : IEquatable<TKey>
    {
        public TKey Value { get; }
    }
}