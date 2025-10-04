namespace ActualChat.Hashing;

public delegate int Hasher<in T>(T source);

public static class Hashers
{
    public static readonly MethodInfo DefaultMethod = typeof(Hashers)
        .GetMethod(nameof(Default), BindingFlags.Static | BindingFlags.Public)!;
    public static readonly MethodInfo ForNullableMethod = typeof(Hashers)
        .GetMethod(nameof(ForNullable), BindingFlags.Static | BindingFlags.Public)!;
    public static readonly MethodInfo ForValueTuple1Method = typeof(Hashers)
        .GetMethod(nameof(ForValueTuple1), BindingFlags.Static | BindingFlags.Public)!;
    public static readonly MethodInfo ForValueTuple2Method = typeof(Hashers)
        .GetMethod(nameof(ForValueTuple2), BindingFlags.Static | BindingFlags.Public)!;
    public static readonly MethodInfo ForValueTuple3Method = typeof(Hashers)
        .GetMethod(nameof(ForValueTuple3), BindingFlags.Static | BindingFlags.Public)!;

    public static Hasher<T> Default<T>()
        => Cache<T>.Default;

    public static Hasher<T?> ForNullable<T>(Hasher<T> baseHasher)
        where T : struct
        => value => value is { } v
            ? baseHasher.Invoke(v)
            : 0;

    public static Hasher<ValueTuple<T1>> ForValueTuple1<T1>(Hasher<T1> h1)
        => value => h1.Invoke(value.Item1);
    public static Hasher<ValueTuple<T1, T2>> ForValueTuple2<T1, T2>(Hasher<T1> h1, Hasher<T2> h2)
        => value => StableHash.Combine(h1.Invoke(value.Item1), h2.Invoke(value.Item2));
    public static Hasher<ValueTuple<T1, T2, T3>> ForValueTuple3<T1, T2, T3>(Hasher<T1> h1, Hasher<T2> h2, Hasher<T3> h3)
        => value => StableHash.Combine(h1.Invoke(value.Item1), h2.Invoke(value.Item2), h3.Invoke(value.Item3));

    // Private methods

    private static class Cache<T>
    {
        public static readonly Hasher<T> Default = static x => x?.GetHashCode() ?? 0;
    }
}
