using System;
using System.Diagnostics.CodeAnalysis;
using Stl;

namespace ActualChat
{
    public static class IdentifierExt
    {
        public static bool IsNullOrEmpty<TIdentifier>(this TIdentifier id)
            where TIdentifier : struct, IIdentifier<string>
            => id.Value.IsNullOrEmpty();
        
        
        public static bool IsNullOrEmpty<TIdentifier>([NotNullWhen(false)]this TIdentifier? id)
            where TIdentifier : struct, IIdentifier<string>
            => id.HasValue && id.Value.IsNullOrEmpty();
        
        public static bool IsNullOrEmpty(this IIdentifier<string> id)
            => id.Value.IsNullOrEmpty();

        public static bool IsNotSet<TKey>(this IIdentifier<TKey> id) where TKey : IEquatable<TKey> 
            => id.Value.Equals(default);
    }
}