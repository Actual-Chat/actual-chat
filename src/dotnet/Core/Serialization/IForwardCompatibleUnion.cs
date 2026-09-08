namespace ActualChat.Serialization;

/// <summary>
/// A <c>[Union]</c> root that survives reading a member this build doesn't know:
/// <see cref="NewUnsupported"/> turns an unrecognized tag into a placeholder instead of
/// letting the whole surrounding payload fail. Registered via
/// <c>ForwardCompatibleUnionFormatter&lt;TSelf&gt;</c>.
/// </summary>
public interface IForwardCompatibleUnion<TSelf>
    where TSelf : class, IForwardCompatibleUnion<TSelf>
{
    /// <param name="tag">The union tag this build has no member for.</param>
    /// <param name="payload">
    /// Positioned at the unknown member's payload — an array of its <c>[Key(N)]</c> values, or a
    /// map keyed by member name under the keyless resolver. Reading from it is optional; the
    /// caller skips the whole envelope afterwards either way.
    /// </param>
    static abstract TSelf? NewUnsupported(
        int tag,
        ref MessagePackReader payload,
        MessagePackSerializerOptions options);
}
