using System;
using Stl.Text;

namespace ActualChat
{
    public static class IdGenerator
    {
        // Probably it's not so strong as 
        // https://github.com/lemire/testingRNG/blob/master/source/splitmix64.h
        // but it doesn't require state and battle-tested
        public static long NewLong() => Math.Abs((long)Guid.NewGuid().GetHashCode() * Guid.NewGuid().GetHashCode());
        
        public static Symbol NewSymbol() => Ulid.NewUlid().ToString();
    }
}